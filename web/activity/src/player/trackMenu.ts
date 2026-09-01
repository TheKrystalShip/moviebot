import type { TrackGroup } from './tracks';

/**
 * A menu built from the manifest rather than from what the media element happens to expose.
 *
 * Feature and commentary are separate groups because a Blu-ray rip routinely carries a dozen of
 * each, and a track the transcode could not produce is listed disabled with its reason: a
 * language simply absent from the menu reads as a bug.
 */
export class TrackMenu {
  readonly el: HTMLElement;

  private readonly button: HTMLButtonElement;
  private readonly popup: HTMLElement;
  private selected: string | null = null;

  constructor(
    private readonly title: string,
    private readonly onSelect: (id: string | null) => void,
    private readonly onToggle: (open: boolean) => void = () => {}
  ) {
    this.el = document.createElement('div');
    this.el.className = 'mb-menu';
    this.el.innerHTML = `
      <button type="button" class="mb-menu__button" aria-haspopup="true" aria-expanded="false">
        <span class="mb-menu__title"></span>
        <span class="mb-menu__value"></span>
      </button>
      <div class="mb-menu__popup" hidden></div>`;

    this.button = this.el.querySelector('.mb-menu__button') as HTMLButtonElement;
    this.popup = this.el.querySelector('.mb-menu__popup') as HTMLElement;
    (this.el.querySelector('.mb-menu__title') as HTMLElement).textContent = title;

    this.button.addEventListener('click', (event) => {
      event.stopPropagation();
      this.toggle(this.popup.hidden);
    });

    document.addEventListener('click', (event) => {
      if (!this.el.contains(event.target as Node)) this.toggle(false);
    });

    this.el.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        this.toggle(false);
        this.button.focus();
      }
    });
  }

  setGroups(groups: TrackGroup[], selected: string | null): void {
    this.selected = selected;
    this.popup.replaceChildren();

    for (const group of groups) {
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
        item.setAttribute('aria-checked', String(entry.id === selected));
        item.classList.toggle('is-selected', entry.id === selected);

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
        } else {
          item.addEventListener('click', (event) => {
            event.stopPropagation();
            this.toggle(false);
            this.onSelect(entry.id);
          });
        }

        section.appendChild(item);
      }

      this.popup.appendChild(section);
    }

    this.showValue(groups);
  }

  private showValue(groups: TrackGroup[]): void {
    const chosen = groups.flatMap((g) => g.entries).find((entry) => entry.id === this.selected);
    const value = this.el.querySelector('.mb-menu__value') as HTMLElement;
    value.textContent = chosen?.label ?? 'Off';
    this.button.setAttribute('aria-label', `${this.title}: ${value.textContent}`);
  }

  private toggle(open: boolean): void {
    if (this.popup.hidden === !open) return;
    this.popup.hidden = !open;
    this.button.setAttribute('aria-expanded', String(open));
    this.el.classList.toggle('mb-menu--open', open);
    this.onToggle(open);
  }
}
