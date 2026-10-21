import { Avatar } from "@thekrystalship/krystal";

const Tones = ["teal", "info", "warn", "ok"];

/** A person's colour, the same one wherever they appear. */
function toneFor(userId) {
  let hash = 0;
  for (const ch of String(userId)) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0;
  return Tones[hash % Tones.length];
}

/** A person's mark: the first letter of their name on a colour that stays theirs. */
export function Person({ person, className = "" }) {
  const title = [person.displayName, person.guest ? "guest" : null, person.buffering ? "buffering" : null].filter(Boolean).join(" · ");
  return (
    <Avatar name={person.displayName || "?"} tone={toneFor(person.userId)} letters={1} size="css" title={title}
      status={person.buffering ? "warn" : undefined} statusLabel={person.buffering ? "buffering" : undefined}
      className={"mp-avatar" + (person.guest ? " is-guest" : "") + (className ? " " + className : "")} />
  );
}
