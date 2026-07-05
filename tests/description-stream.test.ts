import { afterEach, describe, expect, it, vi } from 'vitest';
import { describeStream } from '../src/api/backend-client';

function sseResponse(chunks: string[]): Response {
  const encoder = new TextEncoder();
  const stream = new ReadableStream<Uint8Array>({
    start(controller) {
      for (const chunk of chunks) {
        controller.enqueue(encoder.encode(chunk));
      }
      controller.close();
    },
  });

  return new Response(stream, {
    status: 200,
    headers: { 'Content-Type': 'text/event-stream' },
  });
}

describe('describeStream', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('delivers SSE tokens and completion', async () => {
    const fetchMock = vi.fn().mockResolvedValue(sseResponse([
      'data: {"token":"Ancient "}\n\n',
      'data: {"token":"limestone"}\n\n',
      'data: {"done":true}\n\n',
    ]));
    vi.stubGlobal('fetch', fetchMock);

    const tokens: string[] = [];

    await new Promise<void>((resolve, reject) => {
      describeStream(
        42,
        (token) => tokens.push(token),
        resolve,
        reject,
      );
    });

    expect(tokens).toEqual(['Ancient ', 'limestone']);
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/describe/stream'),
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ cellIndex: 42 }),
      }),
    );
  });

  it('reports HTTP failures to the error callback', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('nope', { status: 500 })));
    const done = vi.fn();

    const error = await new Promise<unknown>((resolve) => {
      describeStream(7, vi.fn(), done, resolve);
    });

    expect(done).not.toHaveBeenCalled();
    expect(error).toBeInstanceOf(Error);
  });
});
