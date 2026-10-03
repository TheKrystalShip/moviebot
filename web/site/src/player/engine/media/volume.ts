/**
 * The curve behind the volume control.
 *
 * `HTMLMediaElement.volume` is a linear amplitude multiplier, and loudness is not linear in
 * amplitude. Halving amplitude is about -6 dB, which people hear as roughly two thirds as loud,
 * so a slider wired straight to that property spends its lower half doing almost nothing and its
 * top quarter doing most of the work.
 *
 * Perceived loudness rises roughly with the 0.6 power of sound pressure, so amplitude has to rise
 * with the 1/0.6 power of the loudness that was asked for. At half the slider that gives
 * 0.5^(5/3) ≈ 0.315, which is about -10 dB — the accepted figure for half as loud.
 *
 * The slider therefore holds the loudness a person asked for, and the media element is given the
 * amplitude that produces it. The two are different numbers on purpose.
 */

const LoudnessExponent = 5 / 3;

/** Slider position (0..1, the loudness asked for) to media amplitude. */
export function amplitudeFor(position: number): number {
  const clamped = Math.min(1, Math.max(0, position));
  return clamped === 0 ? 0 : Math.pow(clamped, LoudnessExponent);
}

/** One key press or one notch of the wheel. */
export const VolumeStep = 0.05;
