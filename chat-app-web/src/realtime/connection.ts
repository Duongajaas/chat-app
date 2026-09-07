import * as signalR from "@microsoft/signalr";
import { getAccessToken } from "../api/client";
import { messagesApi } from "../api/messages";
import { useChatStore } from "../store/chatStore";
import { usePresenceStore } from "../store/presenceStore";

const HUB_URL = import.meta.env.VITE_SIGNALR_HUB_URL;

let hubConnection: signalR.HubConnection | null = null;
let currentUserId: string | null = null;
let connectionStart: Promise<void> | null = null;

function buildConnection() {
  if (!hubConnection) {
    hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(HUB_URL, {
        accessTokenFactory: () => getAccessToken() ?? "",
        withCredentials: true,
      })
      .withAutomaticReconnect()
      .build();
  }

  return hubConnection;
}

export function startChatHub(userId?: string | null) {
  currentUserId = userId ?? null;
  const connection = buildConnection();

  if (connection.state === signalR.HubConnectionState.Connected) {
    return connection;
  }

  if (connection.state === signalR.HubConnectionState.Disconnected) {
    connection.off("UserPresenceChanged");
    connection.off("TypingIndicator");
    connection.off("ReceiveMessage");
    connection.off("MessagesRead");

    connection.on("UserPresenceChanged", (payload: { userId?: string; UserId?: string; isOnline?: boolean; IsOnline?: boolean }) => {
      const userIdValue = String(payload.userId ?? payload.UserId ?? "");
      const isOnline = Boolean(payload.isOnline ?? payload.IsOnline);
      if (!userIdValue) return;

      usePresenceStore.getState().setUserPresence(userIdValue, isOnline);
    });

    connection.on("TypingIndicator", (payload: { conversationId?: string; ConversationId?: string; userId?: string; UserId?: string; isTyping?: boolean; IsTyping?: boolean }) => {
      const conversationId = String(payload.conversationId ?? payload.ConversationId ?? "");
      const senderId = String(payload.userId ?? payload.UserId ?? "");
      const isTyping = Boolean(payload.isTyping ?? payload.IsTyping);

      if (!conversationId || !senderId || senderId === currentUserId) return;
      usePresenceStore.getState().setTypingForConversation(conversationId, isTyping);
    });

    connection.on("ReceiveMessage", (payload: { id?: string; conversationId?: string; ConversationId?: string; senderId?: string; SenderId?: string; sequence?: number; Sequence?: number; content?: string; Content?: string; createdAt?: string; CreatedAt?: string; clientMessageId?: string; ClientMessageId?: string }) => {
      const conversationId = String(payload.conversationId ?? payload.ConversationId ?? "");
      const senderId = String(payload.senderId ?? payload.SenderId ?? "");
      const content = payload.content ?? payload.Content ?? "";
      const createdAt = payload.createdAt ?? payload.CreatedAt ?? new Date().toISOString();
      const sequence = payload.sequence ?? payload.Sequence ?? Number.MAX_VALUE;
      const clientMessageId = payload.clientMessageId ?? payload.ClientMessageId;

      if (!conversationId) return;

      const message = {
        id: payload.id ?? crypto.randomUUID(),
        conversationId,
        senderId,
        sequence,
        content,
        createdAt: new Date(createdAt).toLocaleString("vi-VN", { hour: "2-digit", minute: "2-digit" }),
        ...(clientMessageId && { clientMessageId }),
      };

      useChatStore.getState().appendMessage(conversationId, message);
    });

    connection.on("MessagesRead", (payload: { conversationId?: string; ConversationId?: string; userId?: string; UserId?: string }) => {
      const conversationId = String(payload.conversationId ?? payload.ConversationId ?? "");
      const userIdValue = String(payload.userId ?? payload.UserId ?? "");
      if (!conversationId || !userIdValue) return;
      if (userIdValue !== currentUserId) {
        usePresenceStore.getState().setConversationRead(conversationId);
      }
      usePresenceStore.getState().setTypingForConversation(conversationId, false);
    });

    connection.onreconnected(async () => {
      console.log("SignalR reconnected, syncing messages for offline backlog...");
      try {
        const store = useChatStore.getState();
        const messagesByConversation = store.messagesByConversation;
        const syncNewerMessages = store.syncNewerMessages;
        const setHasMore = store.setHasMore;

        for (const [conversationId, messages] of Object.entries(messagesByConversation)) {
          if (!messages || messages.length === 0) continue;

          // Get latest sequence from current messages
          const latestSequence = Math.max(...messages.map((m) => m.sequence ?? 0));
          if (latestSequence === 0 || latestSequence === Number.MAX_VALUE) continue;

          // Fetch newer messages in batches
          let hasMore = true;
          let currentAfter = latestSequence;

          while (hasMore) {
            try {
              const response = await messagesApi.list(conversationId, undefined, currentAfter, 200);
              const incomingMessages = response.messages.map((item) => ({
                ...item,
                senderId: String(item.senderId),
                createdAt: new Date(item.createdAt).toLocaleString("vi-VN", { hour: "2-digit", minute: "2-digit" }),
              }));

              if (incomingMessages.length > 0) {
                syncNewerMessages(conversationId, incomingMessages);
                const newestSequence = Math.max(...incomingMessages.map((m) => m.sequence ?? 0));
                currentAfter = newestSequence;
              }

              hasMore = response.hasMore ?? false;
              setHasMore(conversationId, hasMore);
            } catch (error) {
              console.error(`Reconnect sync failed for conversation ${conversationId}:`, error);
              break;
            }
          }
        }

        console.log("Message sync complete after reconnect");
      } catch (error) {
        console.error("Reconnect sync error:", error);
      }
    });

    connectionStart = connection.start().catch((error: unknown) => {
      console.error("SignalR connection failed:", error);
      throw error;
    });
  }

  return connection;
}

export async function stopChatHub() {
  if (!hubConnection) return;
  await hubConnection.stop();
  hubConnection = null;
  connectionStart = null;
}

async function getConnectedHub() {
  const connection = buildConnection();
  if (connection.state === signalR.HubConnectionState.Disconnected) {
    startChatHub(currentUserId);
  }
  if (connectionStart) await connectionStart;
  return connection;
}

export async function joinConversation(conversationId: string) {
  const connection = await getConnectedHub();
  await connection.invoke("JoinConversation", conversationId);
}

export async function leaveConversation(conversationId: string) {
  if (!hubConnection) return;
  await hubConnection.invoke("LeaveConversation", conversationId);
}

export async function sendTyping(conversationId: string, isTyping: boolean) {
  const connection = await getConnectedHub();
  await connection.invoke("Typing", conversationId, isTyping);
}

export async function markConversationAsRead(conversationId: string) {
  const connection = await getConnectedHub();
  await connection.invoke("MarkAsRead", conversationId);
}
