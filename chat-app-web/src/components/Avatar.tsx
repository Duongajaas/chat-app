interface AvatarProps {
  name: string;
  color?: string;
  size?: number;
  isOnline?: boolean;
}

function getInitials(name: string): string {
  const parts = name.trim().split(/\s+/);
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

export function Avatar({ name, color = "#33d6a6", size = 40, isOnline }: AvatarProps) {
  return (
    <div className="avatar-wrap" style={{ width: size, height: size }}>
      <div className="avatar-circle" style={{ width: size, height: size, background: color, fontSize: size * 0.38 }}>
        {getInitials(name)}
      </div>
      {isOnline && <span className="avatar-online-dot" />}
    </div>
  );
}