// How long and how long ago, the way a person says them. Every page that prints a duration or a
// moment goes through here so the same span reads the same everywhere.

/** A length or a position: 5760 is "1h 36m". */
export function clock(seconds) {
  const s = Math.max(0, Math.round(seconds || 0));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  return h > 0 ? `${h}h ${String(m).padStart(2, "0")}m` : `${m}m`;
}

/** What is left of a film: "1h 36m left", and "under a minute left" at the very end. */
export function timeLeft(seconds) {
  if (seconds < 60) return "under a minute left";
  return `${clock(seconds)} left`;
}

/** How long ago an instant was: "just now", "3 hours ago", "yesterday", "5 days ago". */
export function ago(iso, now = Date.now()) {
  const seconds = Math.max(0, (now - new Date(iso).getTime()) / 1000);
  if (seconds < 60) return "just now";
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} ${minutes === 1 ? "minute" : "minutes"} ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} ${hours === 1 ? "hour" : "hours"} ago`;
  const days = Math.floor(hours / 24);
  return days === 1 ? "yesterday" : `${days} days ago`;
}

/** The same, starting a sentence. */
export function since(iso) {
  const text = ago(iso);
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** Whole days a film has before it leaves: "3 days left", "1 day left", "leaves today". */
export function daysLeft(days) {
  if (days <= 0) return "leaves today";
  return `${days} ${days === 1 ? "day" : "days"} left`;
}
