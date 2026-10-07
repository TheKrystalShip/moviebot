// The languages a person picks from: the ones a film is commonly offered in, named in the browser's
// own language.

/** The languages a film is commonly offered in, by their two-letter codes. */
export const LANGUAGES = ["en", "es", "fr", "de", "it", "pt", "ca", "nl", "pl", "ru", "ja", "ko", "zh", "ar", "tr"];

/** A language's name in the browser's language, or its code where the browser cannot name it. */
export function languageName(code) {
  try {
    return new Intl.DisplayNames(undefined, { type: "language" }).of(code) || code;
  } catch {
    return code;
  }
}

/**
 * A language picker's options: `none` first (value ""), then the common languages and whatever is
 * stored, so a choice made elsewhere is never shown as something else.
 */
export const languageOptions = (current, none) => [
  { value: "", label: none },
  ...[...new Set([...LANGUAGES, ...(current ? [current] : [])])].map((code) => ({ value: code, label: languageName(code) })),
];
