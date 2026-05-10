// Spec 007 — minimal debounce used by the typing indicator (1s window per FR-026).

export function debounce<TArgs extends unknown[]>(
  fn: (...args: TArgs) => void,
  ms: number,
): (...args: TArgs) => void {
  let timer: ReturnType<typeof setTimeout> | null = null;
  return (...args: TArgs) => {
    if (timer !== null) clearTimeout(timer);
    timer = setTimeout(() => {
      timer = null;
      fn(...args);
    }, ms);
  };
}
