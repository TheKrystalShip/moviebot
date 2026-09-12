import type { AudioPanel } from './audioPanel';
import type { SubtitleMenu } from './subtitleMenu';
import type { SubtitleStylePanel } from './subtitleStylePanel';

type View = 'root' | 'audio' | 'subtitles' | 'style';

const Names: Record<Exclude<View, 'root'>, string> = {
  audio: 'Audio',
  subtitles: 'Subtitles',
  style: 'Subtitle style'
};

const Gear = `<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
  <path fill="currentColor" d="M12 15.5a3.5 3.5 0 1 1 0-7 3.5 3.5 0 0 1 0 7zm7.4-2.6.1-.9-.1-.9 1.9-1.4a.5.5 0 0 0 .1-.6l-1.8-3.1a.5.5 0 0 0-.6-.2l-2.2.9a7 7 0 0 0-1.6-.9l-.3-2.4a.5.5 0 0 0-.5-.4h-3.6a.5.5 0 0 0-.5.4l-.3 2.4a7 7 0 0 0-1.6.9l-2.2-.9a.5.5 0 0 0-.6.2L3.8 9.1a.5.5 0 0 0 .1.6l1.9 1.4-.1.9.1.9-1.9 1.4a.5.5 0 0 0-.1.6l1.8 3.1c.1.2.4.3.6.2l2.2-.9c.5.4 1 .7 1.6.9l.3 2.4c0 .2.2.4.5.4h3.6c.3 0 .5-.2.5-.4l.3-2.4a7 7 0 0 0 1.6-.9l2.2.9c.2.1.5 0 .6-.2l1.8-3.1a.5.5 0 0 0-.1-.6z"/>
</svg>`;

/**
 * One cog holding everything a viewer sets for themselves.
 *
 * Each of the panels behind it is the size of a page, and each is wanted rarely: a track is
 * chosen once for a film and an appearance once for a pair of eyes. Behind a cog they are a row
 * apiece until somebody asks for one, which is what keeps the control bar to the controls people
 * reach for while the film is playing.
 *
 * The panels inside it render their own contents and nothing around them. Opening, closing, the
 * lock that keeps the control bar from fading, and finding the way back out are all here, so
 * there is one of each rather than one per panel.
 */
export class SettingsMenu {
  readonly el: HTMLElement;

  private readonly button: HTMLButtonElement;
  private readonly popup: HTMLElement;
  private view: View = 'root';

  constructor(
    private readonly audio: AudioPanel,
    private readonly subtitles: SubtitleMenu,
    private readonly style: SubtitleStylePanel,
    private readonly onToggle: (open: boolean) => void = () => {}
  ) {
    this.el = document.createElement('div');
    this.el.className = 'mb-menu mb-settings';
    this.el.innerHTML = `
      <button type="button" class="mb-settings__button" aria-haspopup="true" aria-expanded="false"
              aria-label="Settings" title="Settings">${Gear}</button>
      <div class="mb-menu__popup" hidden></div>`;

    this.button = this.el.querySelector('.mb-settings__button') as HTMLButtonElement;
    this.popup = this.el.querySelector('.mb-menu__popup') as HTMLElement;

    this.button.addEventListener('click', (event) => {
      event.stopPropagation();
      this.open(this.popup.hidden);
    });

    document.addEventListener('click', (event) => {
      if (!this.el.contains(event.target as Node)) this.open(false);
    });

    this.el.addEventListener('keydown', (event) => {
      if (event.key !== 'Escape') return;

      // Out of a panel first, and out of the menu only from the top. Escaping the whole thing from
      // three rows deep loses the place somebody was in for one keypress too many.
      if (this.view === 'root') {
        this.open(false);
        this.button.focus();
      } else {
        this.show('root');
      }
    });
  }

  /** Closed from inside, once a choice has been made in one of the panels. */
  close(): void {
    this.open(false);
  }

  /** Redraws the root list, so the value beside each panel's name stays current. */
  refresh(): void {
    if (this.view === 'root' && !this.popup.hidden) this.render();
  }

  private open(open: boolean): void {
    if (this.popup.hidden === !open) return;

    this.popup.hidden = !open;
    this.button.setAttribute('aria-expanded', String(open));
    this.el.classList.toggle('mb-menu--open', open);

    // Always back at the top. A menu that reopens where it was left is one nobody can predict.
    if (open) this.show('root');
    else this.view = 'root';

    this.onToggle(open);
  }

  private show(view: View): void {
    this.view = view;
    this.render();

    if (view === 'subtitles') this.subtitles.shown();
    if (view === 'style') this.style.shown();
  }

  private render(): void {
    this.popup.replaceChildren();

    if (this.view === 'root') {
      this.popup.appendChild(this.entry('Audio', this.audio.value, 'audio'));
      this.popup.appendChild(this.entry('Subtitles', this.subtitles.value, 'subtitles'));
      this.popup.appendChild(this.entry('Subtitle style', this.style.value, 'style'));
      return;
    }

    this.popup.appendChild(this.back(Names[this.view]));
    this.popup.appendChild(this.panel(this.view));
  }

  private panel(view: Exclude<View, 'root'>): HTMLElement {
    if (view === 'audio') return this.audio.el;
    return view === 'subtitles' ? this.subtitles.el : this.style.el;
  }

  private entry(name: string, value: string, view: View): HTMLElement {
    const item = document.createElement('button');
    item.type = 'button';
    item.className = 'mb-menu__item mb-settings__entry';
    item.setAttribute('aria-haspopup', 'true');

    const label = document.createElement('span');
    label.className = 'mb-menu__item-label';
    label.textContent = name;

    const current = document.createElement('span');
    current.className = 'mb-settings__value';
    current.textContent = value;

    const chevron = document.createElement('span');
    chevron.className = 'mb-settings__chevron';
    chevron.setAttribute('aria-hidden', 'true');
    chevron.textContent = '›';

    item.append(label, current, chevron);
    item.addEventListener('click', (event) => {
      event.stopPropagation();
      this.show(view);
    });

    return item;
  }

  private back(name: string): HTMLElement {
    const item = document.createElement('button');
    item.type = 'button';
    item.className = 'mb-settings__back';
    item.setAttribute('aria-label', `Back to settings from ${name}`);

    const chevron = document.createElement('span');
    chevron.className = 'mb-settings__chevron';
    chevron.setAttribute('aria-hidden', 'true');
    chevron.textContent = '‹';

    const label = document.createElement('span');
    label.textContent = name;

    item.append(chevron, label);
    item.addEventListener('click', (event) => {
      event.stopPropagation();
      this.show('root');
    });

    return item;
  }
}
