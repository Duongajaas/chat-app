import { NavLink, useLocation } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { Avatar } from "./Avatar";
import { ChatBubbleIcon, UsersIcon, SettingsIcon } from "./icons";

export function MobileNav() {
  const { user } = useAuth();
  const { pathname } = useLocation();
  const personal = ["/profile", "/blocked-users"].includes(pathname);
  const settings = ["/settings", "/devices", "/friend-link"].includes(pathname);
  return <nav className="mobile-nav" aria-label="Điều hướng chính">
    <NavLink to="/" end><ChatBubbleIcon /><span>Trò chuyện</span></NavLink>
    <NavLink to="/friends"><UsersIcon /><span>Danh bạ</span></NavLink>
    <NavLink to="/profile" aria-label="Cá nhân" className={personal ? "active" : undefined}>
      <Avatar name={user?.fullName || user?.username || "?"} size={26} />
      <span>Cá nhân</span>
    </NavLink>
    <NavLink to="/settings" aria-label="Cài đặt" className={settings ? "active" : undefined}><SettingsIcon /><span>Cài đặt</span></NavLink>
  </nav>;
}
