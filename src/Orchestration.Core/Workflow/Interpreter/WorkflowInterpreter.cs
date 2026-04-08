using System.Text.Json;
using Orchestration.Core.Models;
using Orchestration.Core.Workflow.StateTypes;

namespace Orchestration.Core.Workflow.Interpreter;

/// <summary>
/// Interprets and executes workflow state definitions.
/// </summary>
public sealed class WorkflowInterpreter : IWorkflowInterpreter
{
    private readonly IJsonPathResolver _jsonPathResolver;

    public WorkflowInterpreter(IJsonPathResolver jsonPathResolver)
    {
        _jsonPathResolver = jsonPathResolver;
    }

    /// <inheritdoc />
    public WorkflowDecision EvaluateNext(
        WorkflowDefinition definition,
        WorkflowRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(state);

        if (state.PendingDecision != null)
        {
            throw new InvalidOperationException(
                $"Cannot evaluate the next workflow decision while '{state.PendingDecision.Kind}' is still pending.");
        }

        if (state.IsCompensating)
        {
            return EvaluateCompensationDecision(definition, state);
        }

        if (ShouldFailFromCapturedError(state))
        {
            return BuildFailureDecisionFromState(definition, state);
        }

        var currentStep = state.CurrentStep ?? definition.StartAt;

        while (true)
        {
            var stateDefinition = GetStateDefinition(definition, currentStep);
            state.CurrentStep = currentStep;

            switch (stateDefinition)
            {
                case TaskStateDefinition taskState:
                    return SetPendingDecision(
                        state,
                        CreateTaskDecision(currentStep, taskState, definition, state));

                case WaitStateDefinition waitState when waitState.ExternalEvent != null:
                    return SetPendingDecision(
                        state,
                        CreateWaitForEventDecision(currentStep, waitState, state));

                case WaitStateDefinition waitState:
                    return SetPendingDecision(
                        state,
                        CreateDelayUntilDecision(currentStep, waitState, state));

                case ChoiceStateDefinition choiceState:
                    currentStep = ExecuteChoiceState(choiceState, state);
                    continue;

                case CompensationStateDefinition compensationState:
                    return EvaluateCompensationDecision(definition, state, currentStep, compensationState);

                case SucceedStateDefinition succeedState:
                    return new CompleteWorkflowDecision(
                        currentStep,
                        ResolveWorkflowOutput(succeedState, state));

                case FailStateDefinition failState:
                    return new FailWorkflowDecision(
                        currentStep,
                        failState.Error,
                        failState.Cause ?? failState.Error);

                case ParallelStateDefinition:
                    throw new NotSupportedException(
                        "Parallel states are not supported by the decision-based interpreter path.");

                default:
                    throw new InvalidOperationException($"Unknown state type: {stateDefinition.Type}");
            }
        }
    }

    /// <inheritdoc />
    public void ApplyOutcome(
        WorkflowDefinition definition,
        WorkflowRuntimeState state,
        WorkflowDecision decision,
        WorkflowDecisionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(outcome);

        if (state.PendingDecision == null)
        {
            throw new InvalidOperationException("Cannot apply an outcome when no pending decision exists.");
        }

        var pendingDecision = state.PendingDecision;
        if (pendingDecision.GetType() != decision.GetType() ||
            !string.Equals(pendingDecision.StateName, decision.StateName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot apply outcome for decision '{decision.Kind}' on state '{decision.StateName}' while '{pendingDecision.Kind}' on state '{pendingDecision.StateName}' is pending.");
        }

        decision = pendingDecision;

        switch (decision)
        {
            case ExecuteActivityDecision executeDecision when outcome is ActivityCompletedOutcome completedOutcome:
                ApplyActivityCompletedOutcome(definition, state, executeDecision, completedOutcome);
                break;

            case ExecuteActivityDecision executeDecision when outcome is ActivityFailedOutcome failedOutcome:
                ApplyActivityFailedOutcome(definition, state, executeDecision, failedOutcome);
                break;

            case WaitForEventDecision waitDecision when outcome is EventReceivedOutcome eventOutcome:
                ApplyEventReceivedOutcome(definition, state, waitDecision, eventOutcome);
                break;

            case WaitForEventDecision waitDecision when outcome is DelayTimedOutOutcome delayOutcome:
                ApplyEventWaitTimedOutOutcome(definition, state, waitDecision, delayOutcome);
                break;

            case DelayUntilDecision delayDecision when outcome is DelayTimedOutOutcome delayOutcome:
                ApplyDelayElapsedOutcome(definition, state, delayDecision, delayOutcome);
                break;

            case CompleteWorkflowDecision:
            case FailWorkflowDecision:
                throw new InvalidOperationException(
                    $"Decision '{decision.Kind}' is terminal and does not accept an applied outcome.");

            default:
                throw new InvalidOperationException(
                    $"Outcome '{outcome.Kind}' is not valid for decision '{decision.Kind}'.");
        }
    }

    /// <inheritdoc />
    public async Task<string?> ExecuteStepAsync(
        IWorkflowExecutionContext context,
        WorkflowDefinition definition,
        string currentStep,
        WorkflowRuntimeState state)
    {
        if (!definition.States.TryGetValue(currentStep, out var stateDefinition))
        {
            throw new InvalidOperationException($"State '{currentStep}' not found in workflow definition.");
        }

        state.CurrentStep = currentStep;

        try
        {
            var nextStep = stateDefinition switch
            {
                TaskStateDefinition taskState => await ExecuteTaskStateAsync(context, taskState, state, definition),
                WaitStateDefinition waitState => await ExecuteWaitStateAsync(context, waitState, state),
                ChoiceStateDefinition choiceState => ExecuteChoiceState(choiceState, state),
                ParallelStateDefinition parallelState => await ExecuteParallelStateAsync(context, parallelState, state, definition),
                CompensationStateDefinition compensationState => await ExecuteCompensationStateAsync(context, compensationState, state),
                SucceedStateDefinition succeedState => ExecuteSucceedState(succeedState, state),
                FailStateDefinition failState => ExecuteFailState(failState, state),
                _ => throw new InvalidOperationException($"Unknown state type: {stateDefinition.Type}")
            };

            return nextStep;
        }
        catch (Exception ex) when (stateDefinition is TaskStateDefinition taskState && taskState.Catch?.Any() == true)
        {
            return HandleCatch(taskState.Catch, ex, state);
        }
    }

    private async Task<string?> ExecuteTaskStateAsync(
        IWorkflowExecutionContext context,
        TaskStateDefinition taskState,
        WorkflowRuntimeState state,
        WorkflowDefinition definition)
    {
        var input = WorkflowRuntimeValueNormalizer.Normalize(
            _jsonPathResolver.ResolveInput(taskState.Input, state),
            $"$.states.{state.CurrentStep}.input");
        var retry = taskState.Retry ?? definition.Config.DefaultRetryPolicy;

        object? result;
        try
        {
            result = WorkflowRuntimeValueNormalizer.Normalize(
                await context.CallActivityAsync<object?>(taskState.Activity, input, retry),
                $"$.states.{state.CurrentStep}.output");
        }
        catch (Exception ex)
        {
            state.Error = new WorkflowError
            {
                Message = ex.Message,
                StepName = state.CurrentStep,
                ActivityName = taskState.Activity,
                StackTrace = ex.StackTrace
            };
            throw;
        }

        if (!string.IsNullOrEmpty(taskState.ResultPath))
        {
            _jsonPathResolver.SetValue(taskState.ResultPath, result, state);
        }

        if (!string.IsNullOrEmpty(taskState.CompensateWith))
        {
            state.ExecutedSteps.Add(new ExecutedStep
            {
                StepName = state.CurrentStep!,
                StepType = taskState.Type,
                ActivityName = taskState.Activity,
                CompensationActivity = taskState.CompensateWith,
                Input = input,
                Output = result
            });
        }

        return taskState.End ? null : taskState.Next;
    }

    private async Task<string?> ExecuteWaitStateAsync(
        IWorkflowExecutionContext context,
        WaitStateDefinition waitState,
        WorkflowRuntimeState state)
    {
        if (waitState.ExternalEvent != null)
        {
            return await ExecuteExternalEventWaitAsync(context, waitState.ExternalEvent, state, waitState);
        }

        if (waitState.Seconds.HasValue)
        {
            await context.CreateTimerAsync(TimeSpan.FromSeconds(waitState.Seconds.Value));
        }
        else if (!string.IsNullOrEmpty(waitState.SecondsPath))
        {
            var seconds = _jsonPathResolver.Resolve<int>(waitState.SecondsPath, state);
            await context.CreateTimerAsync(TimeSpan.FromSeconds(seconds));
        }
        else if (waitState.Timestamp.HasValue)
        {
            await context.CreateTimerAsync(waitState.Timestamp.Value);
        }
        else if (!string.IsNullOrEmpty(waitState.TimestampPath))
        {
            var timestamp = _jsonPathResolver.Resolve<DateTimeOffset>(waitState.TimestampPath, state);
            await context.CreateTimerAsync(timestamp);
        }

        return waitState.End ? null : waitState.Next;
    }

    private async Task<string?> ExecuteExternalEventWaitAsync(
        IWorkflowExecutionContext context,
        ExternalEventWait eventWait,
        WorkflowRuntimeState state,
        WaitStateDefinition waitState)
    {
        var timeout = eventWait.TimeoutSeconds.HasValue
            ? TimeSpan.FromSeconds(eventWait.TimeoutSeconds.Value)
            : (TimeSpan?)null;

        try
        {
            var eventData = WorkflowRuntimeValueNormalizer.Normalize(
                await context.WaitForExternalEventAsync<object?>(eventWait.EventName, timeout),
                $"$.events.{eventWait.EventName}");

            if (!string.IsNullOrEmpty(eventWait.ResultPath))
            {
                _jsonPathResolver.SetValue(eventWait.ResultPath, eventData, state);
            }

            return waitState.End ? null : waitState.Next;
        }
        catch (TimeoutException)
        {
            if (!string.IsNullOrEmpty(eventWait.TimeoutNext))
            {
                return eventWait.TimeoutNext;
            }
            throw;
        }
    }

    private async Task<string?> ExecuteParallelStateAsync(
        IWorkflowExecutionContext context,
        ParallelStateDefinition parallelState,
        WorkflowRuntimeState state,
        WorkflowDefinition definition)
    {
        var branchTasks = parallelState.Branches.Select(async branch =>
        {
            var branchState = new WorkflowRuntimeState
            {
                Input = state.Input,
                Variables = WorkflowRuntimeValueNormalizer.NormalizeDictionary(
                    state.Variables,
                    $"$.parallel.{branch.Name}.variables") ?? new Dictionary<string, object?>(),
                System = state.System
            };

            var currentStep = branch.StartAt;
            while (currentStep != null)
            {
                if (!branch.States.TryGetValue(currentStep, out _))
                    break;

                var branchDef = new WorkflowDefinition
                {
                    Id = definition.Id,
                    Name = $"{definition.Name}_{branch.Name}",
                    Version = definition.Version,
                    StartAt = branch.StartAt,
                    States = branch.States,
                    Config = definition.Config
                };

                currentStep = await ExecuteStepAsync(context, branchDef, currentStep, branchState);
            }

            return new { Branch = branch.Name, State = branchState };
        });

        var results = await context.WhenAllAsync(branchTasks.ToArray());

        if (!string.IsNullOrEmpty(parallelState.ResultPath))
        {
            var branchResults = results.ToDictionary(
                r => r.Branch,
                r => (object?)WorkflowRuntimeValueNormalizer.NormalizeDictionary(
                    r.State.StepResults,
                    $"$.parallel.{r.Branch}.stepResults"));
            _jsonPathResolver.SetValue(parallelState.ResultPath, branchResults, state);
        }

        return parallelState.End ? null : parallelState.Next;
    }

    private async Task<string?> ExecuteCompensationStateAsync(
        IWorkflowExecutionContext context,
        CompensationStateDefinition compensationState,
        WorkflowRuntimeState state)
    {
        foreach (var step in compensationState.Steps)
        {
            if (!string.IsNullOrEmpty(step.Condition))
            {
                var conditionResult = _jsonPathResolver.Resolve<bool>(step.Condition, state);
                if (!conditionResult)
                    continue;
            }

            try
            {
                var input = WorkflowRuntimeValueNormalizer.Normalize(
                    _jsonPathResolver.ResolveInput(step.Input, state),
                    $"$.compensation.{state.CurrentStep}.input");
                await context.CallActivityAsync<object?>(step.Activity, input);
            }
            catch (Exception) when (compensationState.ContinueOnError)
            {
                // Intentionally ignored for the legacy Azure path.
            }
        }

        if (!string.IsNullOrEmpty(compensationState.FinalState))
        {
            return compensationState.FinalState;
        }

        return compensationState.End ? null : compensationState.Next;
    }

    private string? ExecuteSucceedState(SucceedStateDefinition succeedState, WorkflowRuntimeState state)
    {
        if (succeedState.Output != null)
        {
            var output = WorkflowRuntimeValueNormalizer.Normalize(
                _jsonPathResolver.ResolveInput(succeedState.Output, state),
                "$.variables.output");
            _jsonPathResolver.SetValue("$.variables.output", output, state);
        }

        return null;
    }

    private string? ExecuteFailState(FailStateDefinition failState, WorkflowRuntimeState state)
    {
        state.Error = new WorkflowError
        {
            Message = failState.Cause ?? failState.Error,
            Code = failState.Error,
            StepName = state.CurrentStep
        };

        throw new WorkflowFailedException(failState.Error, failState.Cause);
    }

    private ExecuteActivityDecision CreateTaskDecision(
        string stateName,
        TaskStateDefinition taskState,
        WorkflowDefinition definition,
        WorkflowRuntimeState state)
    {
        var input = WorkflowRuntimeValueNormalizer.Normalize(
            _jsonPathResolver.ResolveInput(taskState.Input, state),
            $"$.states.{stateName}.input");
        return new ExecuteActivityDecision(
            stateName,
            taskState.Activity,
            input,
            taskState.Retry ?? definition.Config.DefaultRetryPolicy,
            taskState.TimeoutSeconds);
    }

    private WaitForEventDecision CreateWaitForEventDecision(
        string stateName,
        WaitStateDefinition waitState,
        WorkflowRuntimeState state)
    {
        var eventWait = waitState.ExternalEvent
            ?? throw new InvalidOperationException("Expected an external event wait definition.");

        DateTimeOffset? timeoutAt = null;
        if (eventWait.TimeoutSeconds.HasValue)
        {
            timeoutAt = state.System.CurrentTime.AddSeconds(eventWait.TimeoutSeconds.Value);
        }

        return new WaitForEventDecision(stateName, eventWait.EventName, timeoutAt);
    }

    private DelayUntilDecision CreateDelayUntilDecision(
        string stateName,
        WaitStateDefinition waitState,
        WorkflowRuntimeState state)
    {
        if (waitState.Seconds.HasValue)
        {
            return new DelayUntilDecision(stateName, state.System.CurrentTime.AddSeconds(waitState.Seconds.Value));
        }

        if (!string.IsNullOrEmpty(waitState.SecondsPath))
        {
            var seconds = _jsonPathResolver.Resolve<int>(waitState.SecondsPath, state);
            return new DelayUntilDecision(stateName, state.System.CurrentTime.AddSeconds(seconds));
        }

        if (waitState.Timestamp.HasValue)
        {
            return new DelayUntilDecision(stateName, waitState.Timestamp.Value);
        }

        if (!string.IsNullOrEmpty(waitState.TimestampPath))
        {
            var timestamp = _jsonPathResolver.Resolve<DateTimeOffset>(waitState.TimestampPath, state);
            return new DelayUntilDecision(stateName, timestamp);
        }

        throw new InvalidOperationException($"Wait state '{stateName}' does not define a delay boundary.");
    }

    private WorkflowDecision EvaluateCompensationDecision(
        WorkflowDefinition definition,
        WorkflowRuntimeState state)
    {
        var compensationStateName = state.CompensationStateName
            ?? state.CurrentStep
            ?? definition.Config.CompensationState
            ?? throw new InvalidOperationException(
                "Workflow is in compensation mode but no compensation state is available.");

        var compensationState = GetStateDefinition(definition, compensationStateName) as CompensationStateDefinition
            ?? throw new InvalidOperationException(
                $"State '{compensationStateName}' is not a compensation state.");

        return EvaluateCompensationDecision(definition, state, compensationStateName, compensationState);
    }

    private WorkflowDecision EvaluateCompensationDecision(
        WorkflowDefinition definition,
        WorkflowRuntimeState state,
        string stateName,
        CompensationStateDefinition compensationState)
    {
        state.IsCompensating = true;
        state.CompensationStateName = stateName;
        state.CurrentStep = stateName;

        while (state.CompensationStepIndex < compensationState.Steps.Count)
        {
            var stepIndex = state.CompensationStepIndex;
            var step = compensationState.Steps[stepIndex];

            if (!string.IsNullOrEmpty(step.Condition))
            {
                var shouldRun = _jsonPathResolver.Resolve<bool>(step.Condition, state);
                if (!shouldRun)
                {
                    state.CompensationStepIndex++;
                    continue;
                }
            }

            var input = WorkflowRuntimeValueNormalizer.Normalize(
                _jsonPathResolver.ResolveInput(step.Input, state),
                $"$.compensation.{stateName}.steps[{stepIndex}].input");
            var decision = new ExecuteActivityDecision(
                stateName,
                step.Activity,
                input,
                Retry: null,
                TimeoutSeconds: null,
                IsCompensation: true,
                CompensationStepIndex: stepIndex,
                CompensationStepName: step.Name);

            return SetPendingDecision(state, decision);
        }

        state.IsCompensating = false;
        state.CompensationStateName = null;
        state.CompensationStepIndex = 0;

        if (!string.IsNullOrEmpty(compensationState.FinalState))
        {
            state.CurrentStep = compensationState.FinalState;
            return EvaluateNext(definition, state);
        }

        if (!string.IsNullOrEmpty(compensationState.Next))
        {
            state.CurrentStep = compensationState.Next;
            return EvaluateNext(definition, state);
        }

        return BuildFailureDecisionFromState(definition, state);
    }

    private void ApplyActivityCompletedOutcome(
        WorkflowDefinition definition,
        WorkflowRuntimeState state,
        ExecuteActivityDecision decision,
        ActivityCompletedOutcome outcome)
    {
        var normalizedOutput = WorkflowRuntimeValueNormalizer.Normalize(
            outcome.Output,
            $"$.states.{decision.StateName}.output");

        if (decision.IsCompensation)
        {
            if (!string.IsNullOrEmpty(decision.CompensationStepName))
            {
                state.CompletedCompensationSteps.Add(decision.CompensationStepName);
            }

            state.CompensationStepIndex = (decision.CompensationStepIndex ?? state.CompensationStepIndex) + 1;
            state.CurrentStep = decision.StateName;
            state.PendingDecision = null;
            return;
        }

        var taskState = GetStateDefinition(definition, decision.StateName) as TaskStateDefinition
            ?? throw new InvalidOperationException(
                $"State '{decision.StateName}' is not a task state.");

        if (!string.IsNullOrEmpty(taskState.ResultPath))
        {
            _jsonPathResolver.SetValue(taskState.ResultPath, normalizedOutput, state);
        }

        if (!string.IsNullOrEmpty(taskState.CompensateWith))
        {
            state.ExecutedSteps.Add(new ExecutedStep
            {
                StepName = decision.StateName,
                StepType = taskState.Type,
                ActivityName = taskState.Activity,
                CompensationActivity = taskState.CompensateWith,
                Input = decision.Input,
                Output = normalizedOutput
            });
        }

        state.CurrentStep = taskState.End ? null : taskState.Next;
        state.PendingDecision = null;
    }

    private void ApplyActivityFailedOutcome(
        WorkflowDefinition definition,
        WorkflowRuntimeState state,
        ExecuteActivityDecision decision,
        ActivityFailedOutcome outcome)
    {
        if (decision.IsCompensation)
        {
            ApplyCompensationFailureOutcome(definition, state, decision, outcome);
            return;
        }

        var taskState = GetStateDefinition(definition, decision.StateName) as TaskStateDefinition
            ?? throw new InvalidOperationException(
                $"State '{decision.StateName}' is not a task state.");

        var errorType = outcome.ErrorType ?? outcome.ErrorCode ?? "States.TaskFailed";
        state.Error = new WorkflowError
        {
            Message = outcome.ErrorMessage,
            Code = outcome.ErrorCode,
            StepName = decision.StateName,
            ActivityName = decision.ActivityName,
            StackTrace = outcome.StackTrace
        };

        if (TryHandleCatch(taskState.Catch, errorType, outcome.ErrorMessage, state, out var nextStep))
        {
            state.CurrentStep = nextStep;
            state.PendingDecision = null;
            return;
        }

        if (!string.IsNullOrEmpty(definition.Config.CompensationState) &&
            definition.States.ContainsKey(definition.Config.CompensationState))
        {
            EnterCompensationMode(state, definition.Config.CompensationState);
            state.PendingDecision = null;
            return;
        }

        state.CurrentStep = decision.StateName;
        state.PendingDecision = null;
    }

    private void ApplyCompensationFailureOutcome(
        WorkflowDefinition definition,
        WorkflowRuntimeState state,
        ExecuteActivityDecision decision,
        ActivityFailedOutcome outcome)
    {
        var compensationState = GetStateDefinition(definition, decision.StateName) as CompensationStateDefinition
            ?? throw new InvalidOperationException(
                $"State '{decision.StateName}' is not a compensation state.");

        if (compensationState.ContinueOnError)
        {
            state.CompensationStepIndex = (decision.CompensationStepIndex ?? state.CompensationStepIndex) + 1;
            state.CurrentStep = decision.StateName;
            state.PendingDecision = null;
            return;
        }

        state.Error = new WorkflowError
        {
            Message = outcome.ErrorMessage,
            Code = outcome.ErrorCode,
            StepName = decision.StateName,
            ActivityName = decision.ActivityName,
            StackTrace = outcome.StackTrace
        };

        state.IsCompensating = false;
        state.CompensationStateName = null;
        state.CurrentStep = decision.StateName;
        state.PendingDecision = null;
    }

    private void ApplyEventReceivedOutcome(
        WorkflowDefinition definition,
        WorkflowRuntimeState state,
        WaitForEventDecision decision,
        EventReceivedOutcome outcome)
    {
        var normalizedPayload = WorkflowRuntimeValueNormalizer.Normalize(
            outcome.Payload,
            $"$.events.{decision.EventName}");

        var waitState = GetStateDefinition(definition, decision.StateName) as WaitStateDefinition
            ?? throw new InvalidOperationException(
                $"State '{decision.StateName}' is not a wait state.");

        var eventWait = waitState.ExternalEvent
            ?? throw new InvalidOperationException(
                $"State '{decision.StateName}' is not an external event wait.");

        if (!string.IsNullOrEmpty(eventWait.ResultPath))
        {
            _jsonPathResolver.SetValue(eventWait.ResultPath, normalizedPayload, state);
        }

        state.CurrentStep = waitState.End ? null : waitState.Next;
        state.PendingDecision = null;
    }

    private void ApplyEventWaitTimedOutOutcome(
        WorkflowDefinition definition,
        WorkflowRuntimeState state,
        WaitForEventDecision decision,
        DelayTimedOutOutcome _)
    {
        var waitState = GetStateDefinition(definition, decision.StateName) as WaitStateDefinition
            ?? throw new InvalidOperationException(
                $"State '{decision.StateName}' is not a wait state.");

        var eventWait = waitState.ExternalEvent
            ?? throw new InvalidOperationException(
                $"State '{decision.StateName}' is not an external event wait.");

        if (!string.IsNullOrEmpty(eventWait.TimeoutNext))
        {
            state.CurrentStep = eventWait.TimeoutNext;
            state.PendingDecision = null;
            return;
        }

        state.Error = new WorkflowError
        {
            Message = $"External event '{decision.EventName}' timed out.",
            Code = "Timeout",
            StepName = decision.StateName
        };

        if (!string.IsNullOrEmpty(definition.Config.CompensationState) &&
            definition.States.ContainsKey(definition.Config.CompensationState))
        {
            EnterCompensationMode(state, definition.Config.CompensationState);
            state.PendingDecision = null;
            return;
        }

        state.CurrentStep = decision.StateName;
        state.PendingDecision = null;
    }

    private void ApplyDelayElapsedOutcome(
        WorkflowDefinition definition,
        WorkflowRuntimeState state,
        DelayUntilDecision decision,
        DelayTimedOutOutcome _)
    {
        var waitState = GetStateDefinition(definition, decision.StateName) as WaitStateDefinition
            ?? throw new InvalidOperationException(
                $"State '{decision.StateName}' is not a wait state.");

        state.CurrentStep = waitState.End ? null : waitState.Next;
        state.PendingDecision = null;
    }

    private void EnterCompensationMode(
        WorkflowRuntimeState state,
        string compensationStateName)
    {
        state.IsCompensating = true;
        state.CompensationStateName = compensationStateName;
        state.CompensationStepIndex = 0;
        state.CompletedCompensationSteps.Clear();
        state.CurrentStep = compensationStateName;
    }

    private bool ShouldFailFromCapturedError(WorkflowRuntimeState state)
    {
        if (state.Error == null || state.IsCompensating || state.PendingDecision != null)
        {
            return false;
        }

        return state.CurrentStep == null || string.Equals(state.Error.StepName, state.CurrentStep, StringComparison.Ordinal);
    }

    private FailWorkflowDecision BuildFailureDecisionFromState(
        WorkflowDefinition definition,
        WorkflowRuntimeState state)
    {
        var stateName = state.CurrentStep ?? state.Error?.StepName ?? definition.StartAt;
        var errorCode = state.Error?.Code ?? "WorkflowFailed";
        var errorMessage = state.Error?.Message ?? errorCode;

        return new FailWorkflowDecision(stateName, errorCode, errorMessage);
    }

    private WorkflowStateDefinition GetStateDefinition(
        WorkflowDefinition definition,
        string stateName)
    {
        if (!definition.States.TryGetValue(stateName, out var stateDefinition))
        {
            throw new InvalidOperationException($"State '{stateName}' not found in workflow definition.");
        }

        return stateDefinition;
    }

    private TDecision SetPendingDecision<TDecision>(
        WorkflowRuntimeState state,
        TDecision decision)
        where TDecision : WorkflowDecision
    {
        state.PendingDecision = decision;
        return decision;
    }

    private object? ResolveWorkflowOutput(
        SucceedStateDefinition succeedState,
        WorkflowRuntimeState state)
    {
        if (succeedState.Output != null)
        {
            return WorkflowRuntimeValueNormalizer.Normalize(
                _jsonPathResolver.ResolveInput(succeedState.Output, state),
                "$.workflow.output");
        }

        if (state.Variables.TryGetValue("output", out var output))
        {
            return WorkflowRuntimeValueNormalizer.Normalize(output, "$.variables.output");
        }

        return state.StepResults.Count > 0
            ? WorkflowRuntimeValueNormalizer.NormalizeDictionary(state.StepResults, "$.stepResults")
            : null;
    }

    private string ExecuteChoiceState(
        ChoiceStateDefinition choiceState,
        WorkflowRuntimeState state)
    {
        foreach (var choice in choiceState.Choices)
        {
            if (EvaluateCondition(choice.Condition, state))
            {
                return choice.Next;
            }
        }

        if (!string.IsNullOrEmpty(choiceState.Default))
        {
            return choiceState.Default;
        }

        throw new InvalidOperationException("No choice matched and no default specified.");
    }

    private bool EvaluateCondition(ChoiceCondition condition, WorkflowRuntimeState state)
    {
        return condition switch
        {
            ComparisonCondition comparison => EvaluateComparisonCondition(comparison, state),
            LogicalCondition logical => EvaluateLogicalCondition(logical, state),
            _ => false
        };
    }

    private bool EvaluateComparisonCondition(ComparisonCondition condition, WorkflowRuntimeState state)
    {
        var variable = _jsonPathResolver.Resolve(condition.Variable, state);
        var compareValue = !string.IsNullOrEmpty(condition.ValuePath)
            ? _jsonPathResolver.Resolve(condition.ValuePath, state)
            : condition.Value;

        return condition.ComparisonType switch
        {
            ComparisonType.Equals => Equals(variable, ConvertValue(compareValue, variable)),
            ComparisonType.NotEquals => !Equals(variable, ConvertValue(compareValue, variable)),
            ComparisonType.GreaterThan => Compare(variable, compareValue) > 0,
            ComparisonType.GreaterThanOrEquals => Compare(variable, compareValue) >= 0,
            ComparisonType.LessThan => Compare(variable, compareValue) < 0,
            ComparisonType.LessThanOrEquals => Compare(variable, compareValue) <= 0,
            ComparisonType.Contains => variable?.ToString()?.Contains(compareValue?.ToString() ?? "", StringComparison.Ordinal) == true,
            ComparisonType.StartsWith => variable?.ToString()?.StartsWith(compareValue?.ToString() ?? "", StringComparison.Ordinal) == true,
            ComparisonType.EndsWith => variable?.ToString()?.EndsWith(compareValue?.ToString() ?? "", StringComparison.Ordinal) == true,
            ComparisonType.IsNull => variable == null,
            ComparisonType.IsNotNull => variable != null,
            ComparisonType.IsTrue => variable is true || (variable is string s && s.Equals("true", StringComparison.OrdinalIgnoreCase)),
            ComparisonType.IsFalse => variable is false || (variable is string s && s.Equals("false", StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private static object? ConvertValue(object? value, object? targetType)
    {
        if (value == null || targetType == null)
            return value;

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when targetType is int => element.GetInt32(),
                JsonValueKind.Number when targetType is long => element.GetInt64(),
                JsonValueKind.Number => element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => value
            };
        }

        return value;
    }

    private static int Compare(object? left, object? right)
    {
        if (left == null && right == null) return 0;
        if (left == null) return -1;
        if (right == null) return 1;

        if (left is IComparable comparable)
        {
            var rightConverted = Convert.ChangeType(right, left.GetType());
            return comparable.CompareTo(rightConverted);
        }

        return string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }

    private bool EvaluateLogicalCondition(LogicalCondition condition, WorkflowRuntimeState state)
    {
        return condition.LogicalType switch
        {
            LogicalType.And => condition.Conditions?.All(c => EvaluateCondition(c, state)) == true,
            LogicalType.Or => condition.Conditions?.Any(c => EvaluateCondition(c, state)) == true,
            LogicalType.Not => condition.Condition != null && !EvaluateCondition(condition.Condition, state),
            _ => false
        };
    }

    private string? HandleCatch(List<CatchDefinition> catches, Exception ex, WorkflowRuntimeState state)
    {
        var errorType = ex.GetType().Name;

        if (TryHandleCatch(catches, errorType, ex.Message, state, out var nextStep))
        {
            return nextStep;
        }

        throw ex;
    }

    private bool TryHandleCatch(
        List<CatchDefinition>? catches,
        string errorType,
        string errorMessage,
        WorkflowRuntimeState state,
        out string nextStep)
    {
        nextStep = string.Empty;

        if (catches == null)
        {
            return false;
        }

        foreach (var catchDef in catches)
        {
            if (catchDef.Errors.Contains("States.ALL") ||
                catchDef.Errors.Contains(errorType) ||
                catchDef.Errors.Any(e => errorMessage.Contains(e, StringComparison.Ordinal)))
            {
                if (!string.IsNullOrEmpty(catchDef.ResultPath))
                {
                    _jsonPathResolver.SetValue(
                        catchDef.ResultPath,
                        new Dictionary<string, object?>
                        {
                            ["error"] = errorType,
                            ["message"] = errorMessage
                        },
                        state);
                }

                nextStep = catchDef.Next;
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Exception thrown when a workflow explicitly fails via a Fail state.
/// </summary>
public class WorkflowFailedException : Exception
{
    public string ErrorCode { get; }

    public WorkflowFailedException(string errorCode, string? cause)
        : base(cause ?? errorCode)
    {
        ErrorCode = errorCode;
    }
}
