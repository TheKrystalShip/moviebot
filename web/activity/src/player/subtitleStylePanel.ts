import {
  BackgroundColors,
  DefaultSubtitleStyle,
  Edges,
  Fonts,
  LiftRange,
  OpacityRange,
  ScaleRange,
  TextColors,
  isDefaultStyle,
  paintCue,
  type SubtitleStyle,
  type Swatch
} from './subtitleStyle';

/**
 * How subtitles look, as one panel inside the settings menu.
 *
 * Every control is answered by a sample cue at the top of the panel, drawn by the same function
 * that draws the ones over the film and sized as a fraction of its own frame exactly as they are
 * sized as a fraction of theirs. That is the whole reason it is here: a setting judged against a
 * film has to be judged in the seconds a line of dialogue is on screen, and a colour that reads
 * against one shot is not a colour that reads against the next.
 */
export class SubtitleStylePanel {
  readonly el: HTMLElement;

  private readonly sampleBox: HTMLElement;
  private readonly sampleText: HTMLElement;
  private readonly rows: HTMLElement;
  private style: SubtitleStyle;

  constructor(style: SubtitleStyle, private readonly onChange: (style: SubtitleStyle) => void) {
    this.style = style;

    this.el = document.createElement('div');
    this.el.className = 'mb-panel mb-style';

    const frame = document.createElement('div');
    frame.className = 'mb-style__frame';

    this.sampleBox = document.createElement('div');
    this.sampleBox.className = 'mb-style__cue';
    this.sampleText = document.createElement('div');
    this.sampleText.className = 'mb-style__cue-text';
    this.sampleText.textContent = 'Every one of them\nis watching this line.';
    this.sampleBox.appendChild(this.sampleText);
    frame.appendChild(this.sampleBox);

    const sample = document.createElement('div');
    sample.className = 'mb-style__sample';
    sample.setAttribute('aria-hidden', 'true');
    sample.appendChild(frame);

    this.rows = document.createElement('div');
    this.rows.className = 'mb-style__rows';

    this.el.append(sample, this.rows);
    this.render();
  }

  /** What the settings menu shows beside the name of this panel. */
  get value(): string {
    return isDefaultStyle(this.style) ? 'Default' : 'Custom';
  }

  /** Called when the panel comes into view, so the sample is drawn against the frame it has. */
  shown(): void {
    this.paintSample();
  }

  private change(patch: Partial<SubtitleStyle>): void {
    this.style = { ...this.style, ...patch };
    this.onChange(this.style);
    this.render();
  }

  private render(): void {
    this.rows.replaceChildren();

    this.rows.appendChild(this.swatches('Text', TextColors, this.style.textColor,
      (value) => this.change({ textColor: value })));

    this.rows.appendChild(this.swatches('Background', BackgroundColors, this.style.background,
      (value) => this.change({ background: value })));

    // Nothing to be more or less transparent than when there is no background, so the control
    // says so rather than sitting there answering presses that change nothing on screen.
    this.rows.appendChild(this.slider('Opacity', 'backgroundOpacity', OpacityRange,
      this.style.backgroundOpacity, percent, this.style.background === 'none'));

    this.rows.appendChild(this.slider('Size', 'scale', ScaleRange, this.style.scale, percent));

    this.rows.appendChild(this.slider('Position', 'lift', LiftRange, this.style.lift,
      (value) => value === 0 ? 'Bottom' : `+${Math.round(value * 100)}%`));

    this.rows.appendChild(this.choice('Edge', Edges, this.style.edge,
      (value) => this.change({ edge: value })));

    this.rows.appendChild(this.choice('Font', Fonts, this.style.font,
      (value) => this.change({ font: value })));

    this.rows.appendChild(this.choice('Weight',
      [{ value: false, name: 'Normal' }, { value: true, name: 'Bold' }], this.style.bold,
      (value) => this.change({ bold: value })));

    this.rows.appendChild(this.reset());
    this.paintSample();
  }

  /**
   * The sample, drawn by the function that draws a cue over the film.
   *
   * One function for both is what keeps the panel honest. A preview built to look like a cue
   * agrees with the real thing right up to the setting that made somebody open the panel.
   */
  private paintSample(): void {
    paintCue(this.sampleBox, this.sampleText, this.style);
  }

  private row(label: string): HTMLElement {
    const row = document.createElement('div');
    row.className = 'mb-style__row';

    const name = document.createElement('span');
    name.className = 'mb-style__label';
    name.textContent = label;
    row.appendChild(name);

    return row;
  }

  private swatches(
    label: string,
    options: readonly Swatch[],
    selected: string,
    pick: (value: string) => void
  ): HTMLElement {
    const row = this.row(label);

    const group = document.createElement('div');
    group.className = 'mb-style__swatches';
    group.setAttribute('role', 'radiogroup');
    group.setAttribute('aria-label', label);

    for (const option of options) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'mb-style__swatch';
      button.classList.toggle('is-selected', option.value === selected);
      button.setAttribute('role', 'radio');
      button.setAttribute('aria-checked', String(option.value === selected));
      button.setAttribute('aria-label', option.name);
      button.title = option.name;

      if (option.value === 'none') {
        button.classList.add('is-none');
      } else {
        button.style.backgroundColor = option.value;
      }

      button.addEventListener('click', (event) => {
        event.stopPropagation();
        pick(option.value);
      });

      group.appendChild(button);
    }

    row.appendChild(group);
    return row;
  }

  private choice<T extends string | boolean>(
    label: string,
    options: readonly { value: T; name: string }[],
    selected: T,
    pick: (value: T) => void
  ): HTMLElement {
    const row = this.row(label);

    const group = document.createElement('div');
    group.className = 'mb-style__choice';
    group.setAttribute('role', 'radiogroup');
    group.setAttribute('aria-label', label);

    for (const option of options) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'mb-style__option';
      button.classList.toggle('is-selected', option.value === selected);
      button.setAttribute('role', 'radio');
      button.setAttribute('aria-checked', String(option.value === selected));
      button.textContent = option.name;

      button.addEventListener('click', (event) => {
        event.stopPropagation();
        pick(option.value);
      });

      group.appendChild(button);
    }

    row.appendChild(group);
    return row;
  }

  private slider(
    label: string,
    key: 'backgroundOpacity' | 'scale' | 'lift',
    range: { min: number; max: number; step: number },
    value: number,
    format: (value: number) => string,
    disabled = false
  ): HTMLElement {
    const row = this.row(label);
    row.classList.toggle('is-disabled', disabled);

    const input = document.createElement('input');
    input.type = 'range';
    input.className = 'mb-style__slider';
    input.min = String(range.min);
    input.max = String(range.max);
    input.step = String(range.step);
    input.value = String(value);
    input.disabled = disabled;
    input.setAttribute('aria-label', label);

    const readout = document.createElement('span');
    readout.className = 'mb-style__readout';
    readout.textContent = format(value);

    // Drawn while the handle moves and stored when it is let go. Writing storage on every step of
    // a drag writes it a hundred times for one decision.
    input.addEventListener('input', (event) => {
      event.stopPropagation();
      const next = Number(input.value);
      readout.textContent = format(next);
      this.style = { ...this.style, [key]: next };
      this.paintSample();
    });
    input.addEventListener('change', (event) => {
      event.stopPropagation();
      this.change({ [key]: Number(input.value) });
    });

    row.append(input, readout);
    return row;
  }

  private reset(): HTMLElement {
    const row = document.createElement('div');
    row.className = 'mb-style__row mb-style__reset-row';

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'mb-style__reset';
    button.textContent = 'Reset to default';
    button.disabled = isDefaultStyle(this.style);
    button.addEventListener('click', (event) => {
      event.stopPropagation();
      this.change({ ...DefaultSubtitleStyle });
    });

    row.appendChild(button);
    return row;
  }
}

function percent(value: number): string {
  return `${Math.round(value * 100)}%`;
}
