import { useState, useRef, useEffect } from "react";
import { NavLink } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { Avatar } from "./Avatar";
import { ChatBubbleIcon, UsersIcon, SettingsIcon } from "./icons";

export function Sidebar() {
  const { user, logout } = useAuth();
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setMenuOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  return (
    <aside className="app-sidebar">
      <div className="app-sidebar__top">
        <NavLink to="/" end className={({ isActive }) => `sidebar-icon-btn ${isActive ? "is-active" : ""}`} title="Trò chuyện">
          <ChatBubbleIcon />
        </NavLink>
        <NavLink to="/friends" className={({ isActive }) => `sidebar-icon-btn ${isActive ? "is-active" : ""}`} title="Danh bạ">
          <UsersIcon />
        </NavLink>
      </div>

      <div className="app-sidebar__bottom" ref={menuRef}>
        <button className="sidebar-icon-btn" title="Cài đặt (sắp có)" disabled>
          <SettingsIcon />
        </button>

        <div style={{ position: "relative" }}>
          <button className="sidebar-avatar-btn" onClick={() => setMenuOpen((v) => !v)}>
            <Avatar name={user?.fullName || user?.username || "?"} size={38} />
          </button>

          {menuOpen && (
            <div className="user-menu">
              <div className="user-menu__header">
                <Avatar name={user?.fullName || user?.username || "?"} size={40} />
                <div>
                  <div className="user-menu__name">{user?.fullName || user?.username}</div>
                  <div className="user-menu__username">@{user?.username}</div>
                </div>
              </div>
              <NavLink to="/profile" className="user-menu__item" onClick={() => setMenuOpen(false)}>
                Xem trang cá nhân
              </NavLink>
              <NavLink to="/friend-link" className="user-menu__item" onClick={() => setMenuOpen(false)}>
                Link kết bạn
              </NavLink>
              <button className="user-menu__item user-menu__item--danger" onClick={logout}>
                Đăng xuất
              </button>
            </div>
          )}
        </div>
      </div>
    </aside>
  );
}