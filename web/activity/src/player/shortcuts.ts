export interface ShortcutHooks {
  togglePlay(): void;
  seekBy(seconds: number): void;
  toggleFullscreen(): void;
  toggleMute(): void;
  toggleSubtitles(): void;
}

const SmallStepSeconds = 5;
const LargeStepSeconds = 10;

/**
 * The keys a player is expected to answer.
 *
 * Every one of them moves the whole room. There is one playhead and no host, so a space bar here
 * pauses the film for everybody, not for whoever pressed it — which is the premise rather than a
 * hazard, but it is why nothing is bound that a hand resting on a keyboard could trigger, and why
 * the bindings stop at what somebody would deliberately reach for.
 *
 * Nothing is bound while a person is typing, and nothing is bound over a control that already
 * answers the same key: the scrub bar handles the arrows itself when it has focus, and taking
 * them here as well would move the film twice for one press.
 */
export function bindShortcuts(hooks: ShortcutHooks): () => void {
  const onKey = (event: KeyboardEvent): void => {
    if (event.ctrlKey || event.metaKey || event.altKey) return;
    if (isTyping(event.target) || answeredElsewhere(event.target)) return;

    const handled = dispatch(event.key, hooks);
    if (!handled) return;

    // Space scrolls a page and the arrows move a focused control. Neither belongs here once the
    // key has been taken as a command.
    event.preventDefault();
  };

  document.addEventListener('keydown', onKey);
  return () => document.removeEventListener('keydown', onKey);
}

function dispatch(key: string, hooks: ShortcutHooks): boolean {
  switch (key) {
    case ' ':
    case 'k':
    case 'K':
      hooks.togglePlay();
      return true;
    case 'ArrowRight':
      hooks.seekBy(SmallStepSeconds);
      return true;
    case 'ArrowLeft':
      hooks.seekBy(-SmallStepSeconds);
      return true;
    case 'l':
    case 'L':
      hooks.seekBy(LargeStepSeconds);
      return true;
    case 'j':
    case 'J':
      hooks.seekBy(-LargeStepSeconds);
      return true;
    case 'f':
    case 'F':
      hooks.toggleFullscreen();
      return true;
    case 'm':
    case 'M':
      hooks.toggleMute();
      return true;
    case 'c':
    case 'C':
      hooks.toggleSubtitles();
      return true;
    default:
      return false;
  }
}

/** Somebody naming themselves or typing anywhere else is not sending the film a command. */
function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;

  return target.isContentEditable
    || target instanceof HTMLInputElement
    || target instanceof HTMLTextAreaElement
    || target instanceof HTMLSelectElement;
}

/** A control that already answers the key it was given keeps it. */
function answeredElsewhere(target: EventTarget | null): boolean {
  return target instanceof HTMLElement && target.closest('.mb-scrub, .mb-menu') !== null;
}
