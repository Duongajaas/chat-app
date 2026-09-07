import { GoogleLogin, type CredentialResponse } from "@react-oauth/google";
import { useAuth } from "../context/AuthContext";
import { extractErrorMessage } from "../api/auth";

interface GoogleLoginButtonProps {
  onSuccess?: () => void;
  onError?: (message: string) => void;
}

export function GoogleLoginButton({ onSuccess, onError }: GoogleLoginButtonProps) {
  const { googleLogin } = useAuth();

  return (
    <div className="google-btn-wrap">
      <GoogleLogin
        theme="filled_black"
        shape="pill"
        onSuccess={async (credentialResponse: CredentialResponse) => {
          if (!credentialResponse.credential) {
            onError?.("Đăng nhập Google thất bại.");
            return;
          }
          try {
            await googleLogin(credentialResponse.credential);
            onSuccess?.();
          } catch (err) {
            onError?.(extractErrorMessage(err, "Đăng nhập Google thất bại."));
          }
        }}
        onError={() => onError?.("Đăng nhập Google thất bại.")}
      />
    </div>
  );
}
