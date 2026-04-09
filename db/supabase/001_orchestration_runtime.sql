create extension if not exists pgcrypto with schema extensions;

create table if not exists public.workflow_definitions
(
    id              uuid primary key,
    workflow_type   text        not null,
    version         text        not null,
    definition_json jsonb       not null,
    is_latest       boolean     not null default false,
    created_at      timestamptz not null default timezone('utc', now()),
    updated_at      timestamptz not null default timezone('utc', now())
);

create unique index if not exists ux_workflow_definitions_type_version
    on public.workflow_definitions (workflow_type, version);

create unique index if not exists ux_workflow_definitions_latest
    on public.workflow_definitions (workflow_type)
    where is_latest;

create table if not exists public.workflow_instances
(
    instance_id             text primary key,
    definition_id           uuid        not null references public.workflow_definitions (id),
    definition_workflow_type text       not null,
    definition_version      text,
    status                  text        not null,
    current_state_name      text,
    runtime_state           jsonb       not null,
    lease_owner             text,
    lease_expires_at        timestamptz,
    initiated_by_user_id    uuid,
    requested_by_user_id    uuid,
    created_at              timestamptz not null default timezone('utc', now()),
    updated_at              timestamptz not null default timezone('utc', now()),
    completed_at            timestamptz
);

create index if not exists ix_workflow_instances_runnable
    on public.workflow_instances (status, lease_expires_at);

create table if not exists public.onboarding_records
(
    id                   text primary key,
    entity_id            text        not null,
    status               text        not null default 'Created',
    idempotency_key      text,
    data_json            jsonb,
    created_at           timestamptz not null default timezone('utc', now()),
    updated_at           timestamptz,
    workflow_instance_id text
);

create index if not exists ix_onboarding_records_entity_id
    on public.onboarding_records (entity_id);

create unique index if not exists ux_onboarding_records_idempotency_key
    on public.onboarding_records (idempotency_key)
    where idempotency_key is not null;

create table if not exists public.step_executions
(
    step_execution_id text primary key,
    instance_id       text        not null references public.workflow_instances (instance_id) on delete cascade,
    state_name        text        not null,
    activity_name     text,
    attempt           integer     not null,
    is_compensation   boolean     not null default false,
    status            text        not null,
    decision_json     jsonb       not null,
    outcome_json      jsonb,
    error_code        text,
    error_message     text,
    created_at        timestamptz not null default timezone('utc', now()),
    started_at        timestamptz,
    finished_at       timestamptz
);

create index if not exists ix_step_executions_instance_created
    on public.step_executions (instance_id, created_at);

create table if not exists public.workflow_events
(
    event_id           text primary key,
    instance_id        text        not null references public.workflow_instances (instance_id) on delete cascade,
    event_name         text        not null,
    payload            jsonb,
    recorded_at        timestamptz not null default timezone('utc', now()),
    consumed_at        timestamptz,
    consumed_by_state  text
);

create index if not exists ix_workflow_events_unconsumed
    on public.workflow_events (instance_id, recorded_at)
    where consumed_at is null;

grant usage on schema public to postgres, service_role;
grant select, insert, update, delete on all tables in schema public to postgres, service_role;
grant usage, select on all sequences in schema public to postgres, service_role;
alter default privileges in schema public grant select, insert, update, delete on tables to postgres, service_role;
alter default privileges in schema public grant usage, select on sequences to postgres, service_role;

alter table public.workflow_definitions enable row level security;
alter table public.workflow_instances enable row level security;
alter table public.onboarding_records enable row level security;
alter table public.step_executions enable row level security;
alter table public.workflow_events enable row level security;

drop policy if exists workflow_definitions_service_role_all on public.workflow_definitions;
create policy workflow_definitions_service_role_all
on public.workflow_definitions
for all
to public
using (auth.role() = 'service_role')
with check (auth.role() = 'service_role');

drop policy if exists workflow_instances_service_role_all on public.workflow_instances;
create policy workflow_instances_service_role_all
on public.workflow_instances
for all
to public
using (auth.role() = 'service_role')
with check (auth.role() = 'service_role');

drop policy if exists onboarding_records_service_role_all on public.onboarding_records;
create policy onboarding_records_service_role_all
on public.onboarding_records
for all
to public
using (auth.role() = 'service_role')
with check (auth.role() = 'service_role');

drop policy if exists step_executions_service_role_all on public.step_executions;
create policy step_executions_service_role_all
on public.step_executions
for all
to public
using (auth.role() = 'service_role')
with check (auth.role() = 'service_role');

drop policy if exists workflow_events_service_role_all on public.workflow_events;
create policy workflow_events_service_role_all
on public.workflow_events
for all
to public
using (auth.role() = 'service_role')
with check (auth.role() = 'service_role');

create or replace function public.save_workflow_definition(
    p_definition_id uuid,
    p_workflow_type text,
    p_version text,
    p_definition_json jsonb
)
returns uuid
language plpgsql
as
$$
begin
    update public.workflow_definitions
    set is_latest = false,
        updated_at = timezone('utc', now())
    where workflow_type = p_workflow_type
      and version <> p_version
      and is_latest = true;

    insert into public.workflow_definitions as target
    (
        id,
        workflow_type,
        version,
        definition_json,
        is_latest,
        created_at,
        updated_at
    )
    values
    (
        p_definition_id,
        p_workflow_type,
        p_version,
        p_definition_json,
        true,
        timezone('utc', now()),
        timezone('utc', now())
    )
    on conflict (workflow_type, version) do update
        set definition_json = excluded.definition_json,
            is_latest = true,
            updated_at = timezone('utc', now());

    return p_definition_id;
end;
$$;

create or replace function public.delete_workflow_definition(
    p_workflow_type text,
    p_version text
)
returns boolean
language plpgsql
as
$$
declare
    v_deleted_id uuid;
begin
    delete from public.workflow_definitions
    where workflow_type = p_workflow_type
      and version = p_version
    returning id into v_deleted_id;

    if v_deleted_id is null then
        return false;
    end if;

    update public.workflow_definitions
    set is_latest = false,
        updated_at = timezone('utc', now())
    where workflow_type = p_workflow_type;

    update public.workflow_definitions
    set is_latest = true,
        updated_at = timezone('utc', now())
    where id = (
        select id
        from public.workflow_definitions
        where workflow_type = p_workflow_type
        order by updated_at desc, created_at desc
        limit 1
    );

    return true;
end;
$$;

create or replace function public.try_acquire_workflow_lease(
    p_instance_id text,
    p_owner_id text,
    p_expires_at timestamptz
)
returns boolean
language plpgsql
as
$$
declare
    v_updated_count integer;
begin
    update public.workflow_instances
    set lease_owner = p_owner_id,
        lease_expires_at = p_expires_at,
        updated_at = timezone('utc', now())
    where instance_id = p_instance_id
      and (
          lease_owner is null
          or lease_expires_at is null
          or lease_expires_at <= timezone('utc', now())
          or lease_owner = p_owner_id
      );

    get diagnostics v_updated_count = row_count;
    return v_updated_count > 0;
end;
$$;

create or replace function public.renew_workflow_lease(
    p_instance_id text,
    p_owner_id text,
    p_expires_at timestamptz
)
returns boolean
language plpgsql
as
$$
declare
    v_updated_count integer;
begin
    update public.workflow_instances
    set lease_expires_at = p_expires_at,
        updated_at = timezone('utc', now())
    where instance_id = p_instance_id
      and lease_owner = p_owner_id;

    get diagnostics v_updated_count = row_count;
    return v_updated_count > 0;
end;
$$;

create or replace function public.release_workflow_lease(
    p_instance_id text,
    p_owner_id text
)
returns boolean
language plpgsql
as
$$
declare
    v_updated_count integer;
begin
    update public.workflow_instances
    set lease_owner = null,
        lease_expires_at = null,
        updated_at = timezone('utc', now())
    where instance_id = p_instance_id
      and lease_owner = p_owner_id;

    get diagnostics v_updated_count = row_count;
    return v_updated_count > 0;
end;
$$;

grant execute on function public.save_workflow_definition(uuid, text, text, jsonb) to postgres, service_role;
grant execute on function public.delete_workflow_definition(text, text) to postgres, service_role;
grant execute on function public.try_acquire_workflow_lease(text, text, timestamptz) to postgres, service_role;
grant execute on function public.renew_workflow_lease(text, text, timestamptz) to postgres, service_role;
grant execute on function public.release_workflow_lease(text, text) to postgres, service_role;

notify pgrst, 'reload schema';
