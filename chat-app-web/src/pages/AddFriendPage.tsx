import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { Avatar } from "../components/Avatar";
import { extractErrorMessage } from "../api/auth";
import { friendLinksApi, type PublicFriendProfile } from "../api/friendLinks";

export default function AddFriendPage() {
  const { token } = useParams<{ token: string }>();
  const [profile, setProfile] = useState<PublicFriendProfile | null>(null);
  const [error, setError] = useState("");
  const [isSending, setIsSending] = useState(false);

  useEffect(() => {
    if (!token) return;
    friendLinksApi.resolve(token).then(setProfile).catch((reason) => setError(extractErrorMessage(reason)));
  }, [token]);

  async function sendFriendRequest() {
    if (!profile) return;
    setIsSending(true);
    setError("");
    try {
      await friendLinksApi.sendRequest(profile.id);
      setProfile({ ...profile, relationshipStatus: "Pending" });
    } catch (reason) {
      setError(extractErrorMessage(reason));
    } finally {
      setIsSending(false);
    }
  }

  return (
    <main className="friend-link-page">
      <section className="friend-link-card friend-link-card--profile">
        {error ? (
          <><h1>Không thể mở link</h1><div className="alert alert-danger">{error}</div></>
        ) : !profile ? (
          <p className="friend-link-card__loading">Đang tải hồ sơ...</p>
        ) : (
          <>
            <Avatar name={profile.fullName} size={72} />
            <h1>{profile.fullName}</h1>
            <p className="friend-link-card__username">@{profile.username}</p>
            {profile.relationshipStatus === "Friends" && <p className="friend-link-status">Hai bạn đã là bạn bè</p>}
            {profile.relationshipStatus === "Pending" && <p className="friend-link-status">Lời mời kết bạn đang chờ xử lý</p>}
            {profile.relationshipStatus === "None" && <button className="btn-primary" type="button" onClick={sendFriendRequest} disabled={isSending}>{isSending ? "Đang gửi..." : "Gửi lời mời kết bạn"}</button>}
          </>
        )}
        <Link className="profile-back-link" to="/">Mở ChatApp</Link>
      </section>
    </main>
  );
}