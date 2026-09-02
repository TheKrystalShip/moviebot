import type { TrackGroup } from './tracks';

/**
 * The audio track list, as one panel inside the settings menu.
 *
 * It renders a list and nothing around it: the button that opens it, the popup it sits in and the
 * navigation back out all belong to the menu hosting it, so there is one of each rather than one
 * per list.
 *
 * Feature and commentary stay separate groups because a Blu-ray rip routinely carries a dozen of
 * each, and a track the transcode could not produce is listed disabled with its reason — a
 * language simply absent from the menu reads as a bug.
 */
export class AudioPanel {
  readonly el: HTMLElement;

  private selected: string | null = null;
  private groups: TrackGroup[] = [];

  constructor(private readonly onSelect: (id: string) => void) {
    this.el = document.createElement('div');
    this.el.className = 'mb-panel';
  }

  /** What the settings menu shows beside the word Audio. */
  get value(): string {
    return this.groups.flatMap((g) => g.entries).find((e) => e.id === this.selected)?.label ?? '';
  }

  setGroups(groups: TrackGroup[], selected: string | null): void {
    this.groups = groups;
    this.selected = selected;
    this.render();
  }

  private render(): void {
    this.el.replaceChildren();

    for (const group of this.groups) {
      const section = document.createElement('div');
      section.className = 'mb-menu__group';

      if (group.title !== '') {
        const heading = document.createElement('div');
        heading.className = 'mb-menu__heading';
        heading.textContent = group.title;
        section.appendChild(heading);
      }

      for (const entry of group.entries) {
        const item = document.createElement('button');
        item.type = 'button';
        item.className = 'mb-menu__item';
        item.setAttribute('role', 'menuitemradio');
        item.setAttribute('aria-checked', String(entry.id === this.selected));
        item.classList.toggle('is-selected', entry.id === this.selected);

        const label = document.createElement('span');
        label.className = 'mb-menu__item-label';
        label.textContent = entry.label;
        item.appendChild(label);

        if (entry.detail !== undefined) {
          const detail = document.createElement('span');
          detail.className = 'mb-menu__item-detail';
          detail.textContent = entry.detail;
          item.appendChild(detail);
        }

        if (!entry.available) {
          item.disabled = true;
          item.classList.add('is-unavailable');
          item.title = `${entry.label} — ${entry.detail ?? 'unavailable'}`;
        } else if (entry.id !== null) {
          const id = entry.id;
          item.addEventListener('click', (event) => {
            event.stopPropagation();
            this.onSelect(id);
          });
        }

        section.appendChild(item);
      }

      this.el.appendChild(section);
    }
  }
}
