namespace ChatApp.Models;

public enum ConversationType { Direct, Group }
public enum MemberRole { Owner, Admin, Member }
public enum MemberRequestStatus { Pending, Accepted }
public enum MessageType { Text, Image, Video, File, Audio, Sticker, Gif, System, CallLog }
public enum MessageStatus { Sending, Sent, Failed, Deleted }
public enum FriendRequestStatus { Pending, Accepted, Rejected, Canceled }
public enum CallType { Audio, Video }
public enum CallStatus { Ringing, Accepted, Rejected, Missed, Canceled, Ended, Busy, Failed }
public enum CallParticipantStatus { Invited, Ringing, Joined, Left, Rejected, Missed }
public enum DevicePlatform { Web, Android, Ios, Desktop }
public enum ReportTargetType { User, Message, Conversation }
public enum ReportStatus { Pending, Reviewing, Resolved, Dismissed }
public enum NotificationStatus { Created, Sent, Failed, Read }
