import {
  createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode,
} from 'react';
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';
import { tokenStore } from '@/api/client';
import { useAuth } from '@/lib/auth';

type Handler = (payload: unknown) => void;

interface RealtimeContextValue {
  connected: boolean;
  /** Subscribes to a hub event. Returns an unsubscribe function. */
  on: (event: string, handler: Handler) => () => void;
}

const RealtimeContext = createContext<RealtimeContextValue>({
  connected: false,
  on: () => () => {},
});

/**
 * Wraps the SignalR connection for the whole app.
 *
 * One connection is shared by every screen. Opening a socket per component would multiply
 * connections across a dashboard that shows live events, reader status and counters, and the
 * server would fan the same message out several times to the same browser.
 *
 * Handlers are held in a ref-backed map so subscribing does not tear down and rebuild the
 * connection every time a component mounts.
 */
export function RealtimeProvider({ children }: { children: ReactNode }) {
  const [connected, setConnected] = useState(false);
  const connectionRef = useRef<HubConnection | null>(null);
  const handlersRef = useRef(new Map<string, Set<Handler>>());

  // The provider mounts before anyone has signed in. Keying the effect on the authenticated
  // user means the hub connects the moment a session exists, and is torn down and rebuilt on
  // sign-out or when a different account signs in.
  const { isAuthenticated, user } = useAuth();

  useEffect(() => {
    if (!isAuthenticated || !tokenStore.access) {
      setConnected(false);
      return;
    }

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/campus', {
        // The browser cannot set headers on a WebSocket handshake, so the token goes in the
        // query string; the API accepts it only for hub paths.
        accessTokenFactory: () => tokenStore.access ?? '',
      })
      // Backs off rather than hammering a server that may be restarting.
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    // Registered once for every event the app knows about, then dispatched to whoever is
    // currently listening.
    const events = [
      'rfidEvent', 'readerStatus', 'attendanceUpdate', 'dashboardCounters', 'notification',
    ];

    for (const event of events) {
      connection.on(event, (payload: unknown) => {
        handlersRef.current.get(event)?.forEach((handler) => {
          try {
            handler(payload);
          } catch (error) {
            // One bad handler must not stop the others receiving the message.
            console.error(`Realtime handler for "${event}" failed`, error);
          }
        });
      });
    }

    connection.onreconnected(() => setConnected(true));
    connection.onreconnecting(() => setConnected(false));
    connection.onclose(() => setConnected(false));

    connection
      .start()
      .then(() => setConnected(true))
      .catch(() => {
        // Live updates are an enhancement: the app polls and stays fully usable without them.
        setConnected(false);
      });

    return () => {
      void connection.stop();
      connectionRef.current = null;
      setConnected(false);
    };
  }, [isAuthenticated, user?.id]);

  const value = useMemo<RealtimeContextValue>(() => ({
    connected,
    on: (event, handler) => {
      const handlers = handlersRef.current.get(event) ?? new Set<Handler>();
      handlers.add(handler);
      handlersRef.current.set(event, handlers);

      return () => {
        handlers.delete(handler);
      };
    },
  }), [connected]);

  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}

export function useRealtime() {
  return useContext(RealtimeContext);
}

/** Subscribes to one hub event for the lifetime of a component. */
export function useRealtimeEvent<T = unknown>(event: string, handler: (payload: T) => void) {
  const { on } = useRealtime();
  const handlerRef = useRef(handler);
  handlerRef.current = handler;

  useEffect(() => {
    // The stable wrapper means a caller can pass an inline arrow without resubscribing on
    // every render.
    return on(event, (payload) => handlerRef.current(payload as T));
  }, [event, on]);
}

export function useConnectionState() {
  const connection = useContext(RealtimeContext);
  return connection.connected ? HubConnectionState.Connected : HubConnectionState.Disconnected;
}
