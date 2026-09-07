// import type { Conversation, ChatMessage } from "../types";

// export const mockConversations: Conversation[] = [
//   {
//     id: "1",
//     name: "Minh Anh",
//     type: "Direct",
//     avatarColor: "#33d6a6",
//     lastMessage: "Mai họp lúc mấy giờ nhỉ?",
//     lastMessageAt: "2 phút",
//     unreadCount: 2,
//     isOnline: true,
//     requestStatus: "Accepted",
//   },
//   {
//     id: "2",
//     name: "Nhóm Dự án Chat App",
//     type: "Group",
//     avatarColor: "#7fa8ff",
//     lastMessage: "Bạn: đã fix xong bug login rồi",
//     lastMessageAt: "15 phút",
//     unreadCount: 0,
//     isOnline: false,
//     requestStatus: "Accepted",
//   },
//   {
//     id: "3",
//     name: "Hoàng Long",
//     type: "Direct",
//     avatarColor: "#ff9f6b",
//     lastMessage: "Ok để mình check lại",
//     lastMessageAt: "1 giờ",
//     unreadCount: 0,
//     isOnline: false,
//     requestStatus: "Accepted",
//   },
//   {
//     id: "4",
//     name: "Thu Trang",
//     type: "Direct",
//     avatarColor: "#c792ea",
//     lastMessage: "Cảm ơn bạn nhiều nha",
//     lastMessageAt: "Hôm qua",
//     unreadCount: 0,
//     isOnline: true,
//     requestStatus: "Accepted",
//   },
// ];

// export const mockMessages: Record<string, ChatMessage[]> = {
//   "1": [
//     { id: "m1", conversationId: "1", senderId: "1", sequence: 1, content: "Chào bạn, mai mình họp dự án lúc mấy giờ nhỉ?", createdAt: "09:12" },
//     { id: "m2", conversationId: "1", senderId: "user-current", sequence: 2, content: "9h sáng nhé, mình gửi link Meet sau", createdAt: "09:14" },
//     { id: "m3", conversationId: "1", senderId: "1", sequence: 3, content: "Oke bạn, cảm ơn nha", createdAt: "09:15" },
//     { id: "m4", conversationId: "1", senderId: "1", sequence: 4, content: "Mai họp lúc mấy giờ nhỉ?", createdAt: "09:20" },
//   ],
//   "2": [
//     { id: "m5", conversationId: "2", senderId: "2", sequence: 1, content: "Mọi người check PR mới giúp mình với", createdAt: "Hôm qua" },
//     { id: "m6", conversationId: "2", senderId: "user-current", sequence: 2, content: "Đã fix xong bug login rồi", createdAt: "Hôm qua" },
//   ],
// };