import { createContext, useContext, useEffect, useState, useCallback, type ReactNode } from "react";
import { authApi } from "../api/auth";
import { usersApi } from "../api/users";
import { clearAccessToken, refreshAcrossTabs, setAccessToken } from "../api/client";
import { startChatHub, stopChatHub } from "../realtime/connection";
import type { User, AuthResponse, RegisterPayload } from "../types";

interface AuthContextValue {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (username: string, password: string) => Promise<AuthResponse>;
  register: (payload: RegisterPayload) => Promise<AuthResponse>;
  googleLogin: (idToken: string) => Promise<AuthResponse>;
  logout: () => Promise<void>;
  setUser: (user: User) => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;

    async function bootstrap() {
      try {
        await refreshAcrossTabs();
        const userResponse = await usersApi.getMe();

        if (cancelled) return;

        const userData: User = {
          id: userResponse.id,
          username: userResponse.username,
          email: userResponse.email,
          phone: userResponse.phone,
          fullName: userResponse.fullName,
          avatarUrl: userResponse.avatarUrl,
          bio: userResponse.bio,
          isVerified: userResponse.isVerified,
        };

        setUser(userData);
      } catch {
        if (!cancelled) {
          clearAccessToken();
          setUser(null);
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    }

    bootstrap();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    function handleSessionExpired() {
      clearAccessToken();
      setUser(null);
    }

    window.addEventListener("auth:session-expired", handleSessionExpired);
    return () => window.removeEventListener("auth:session-expired", handleSessionExpired);
  }, []);

  useEffect(() => {
    if (!user?.id) {
      stopChatHub();
      return;
    }

    startChatHub(user.id);
    return () => {
      stopChatHub();
    };
  }, [user?.id]);

  const applyAuthResponse = useCallback((data: AuthResponse) => {
    setAccessToken(data.accessToken);
    setUser(data.user);
  }, []);

  const login = useCallback(
    async (username: string, password: string) => {
      const data = await authApi.login({ username, password });
      applyAuthResponse(data);
      return data;
    },
    [applyAuthResponse]
  );

  const register = useCallback(
    async (payload: RegisterPayload) => {
      const data = await authApi.register(payload);
      applyAuthResponse(data);
      return data;
    },
    [applyAuthResponse]
  );

  const googleLogin = useCallback(
    async (idToken: string) => {
      const data = await authApi.googleLogin(idToken);
      applyAuthResponse(data);
      return data;
    },
    [applyAuthResponse]
  );

  const logout = useCallback(async () => {
    try {
      await authApi.logout();
    } catch {
      // Dù API lỗi vẫn clear session phía client
    }
    clearAccessToken();
    setUser(null);
  }, []);

  const value: AuthContextValue = {
    user,
    isAuthenticated: !!user,
    isLoading,
    login,
    register,
    googleLogin,
    logout,
    setUser,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth phải được dùng bên trong AuthProvider");
  return ctx;
}
