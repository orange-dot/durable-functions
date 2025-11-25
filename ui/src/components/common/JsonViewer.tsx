import { useState } from 'react';

interface JsonViewerProps {
  data: unknown;
  initialExpanded?: boolean;
}

export function JsonViewer({ data, initialExpanded = true }: JsonViewerProps) {
  const [expanded, setExpanded] = useState(initialExpanded);

  const jsonString = JSON.stringify(data, null, 2);

  return (
    <div className="relative">
      <div className="absolute top-2 right-2 flex gap-2">
        <button
          onClick={() => setExpanded(!expanded)}
          className="px-2 py-1 text-xs bg-gray-100 text-gray-600 rounded hover:bg-gray-200"
        >
          {expanded ? 'Collapse' : 'Expand'}
        </button>
        <button
          onClick={() => navigator.clipboard.writeText(jsonString)}
          className="px-2 py-1 text-xs bg-gray-100 text-gray-600 rounded hover:bg-gray-200"
        >
          Copy
        </button>
      </div>
      <pre
        className={`bg-gray-50 rounded-lg p-4 text-sm font-mono overflow-auto ${
          expanded ? 'max-h-96' : 'max-h-24'
        }`}
      >
        <code className="text-gray-800">{jsonString}</code>
      </pre>
    </div>
  );
}
