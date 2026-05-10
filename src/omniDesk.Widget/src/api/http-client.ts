// Spec 007 — fetch wrapper that injects the X-Widget-Token + X-Anonymous-Id headers and
// unwraps the success/error envelope from the API.

import type { ApiEnvelope, ApiError } from '../types';

export class WidgetHttpError extends Error {
  constructor(public readonly code: string, public readonly status: number, message: string) {
    super(message);
    this.name = 'WidgetHttpError';
  }
}

export class HttpClient {
  constructor(
    private readonly baseUrl: string,
    private readonly token: string,
    private readonly anonymousId: string,
  ) {}

  get<T>(path: string, params?: Record<string, string | number | undefined>): Promise<T> {
    const url = this.buildUrl(path, params);
    return this.request<T>(url, { method: 'GET' });
  }

  post<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>(this.buildUrl(path), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: body === undefined ? null : JSON.stringify(body),
    });
  }

  private buildUrl(path: string, params?: Record<string, string | number | undefined>): string {
    const url = new URL(`${this.baseUrl.replace(/\/$/, '')}${path}`);
    if (params) {
      for (const [k, v] of Object.entries(params)) {
        if (v !== undefined && v !== null) url.searchParams.set(k, String(v));
      }
    }
    return url.toString();
  }

  private async request<T>(url: string, init: RequestInit): Promise<T> {
    const headers = new Headers(init.headers);
    headers.set('X-Widget-Token', this.token);
    headers.set('X-Anonymous-Id', this.anonymousId);
    headers.set('Accept', 'application/json');

    const response = await fetch(url, { ...init, headers, credentials: 'omit' });
    let payload: ApiEnvelope<T> | ApiError;
    try {
      payload = (await response.json()) as ApiEnvelope<T> | ApiError;
    } catch {
      throw new WidgetHttpError('INVALID_RESPONSE', response.status, 'Server returned non-JSON response.');
    }

    if (response.ok && 'success' in payload && payload.success) {
      return payload.data;
    }

    if ('error' in payload) {
      throw new WidgetHttpError(payload.error.code, response.status, payload.error.message);
    }
    throw new WidgetHttpError('UNKNOWN', response.status, `HTTP ${response.status}`);
  }
}
