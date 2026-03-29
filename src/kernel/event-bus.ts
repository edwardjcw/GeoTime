import type { GeoEventType, GeoEventPayloadMap } from '../shared/types';

type Callback<T extends GeoEventType> = (payload: GeoEventPayloadMap[T]) => void;

export class EventBus {
  private listeners = new Map<GeoEventType, Set<Callback<never>>>();

  on<T extends GeoEventType>(type: T, callback: Callback<T>): () => void {
    if (!this.listeners.has(type)) {
      this.listeners.set(type, new Set());
    }
    const set = this.listeners.get(type)!;
    set.add(callback as Callback<never>);
    return () => set.delete(callback as Callback<never>);
  }

  off<T extends GeoEventType>(type: T, callback: Callback<T>): void {
    this.listeners.get(type)?.delete(callback as Callback<never>);
  }

  emit<T extends GeoEventType>(type: T, payload: GeoEventPayloadMap[T]): void {
    const set = this.listeners.get(type);
    if (!set) return;
    for (const cb of set) {
      (cb as Callback<T>)(payload);
    }
  }

  clear(): void {
    this.listeners.clear();
  }
}
