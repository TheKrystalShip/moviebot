// The Krystal config, raised to errors: a literal colour, a radius off the scale or a hand-written
// surface border fails `npm run lint:css`.
export default {
  extends: ["@thekrystalship/krystal/stylelint"],
  defaultSeverity: "error",
  rules: {
    // The package's `.k-` reach-in rule, an error: a sheet styles its own classes.
    "selector-disallowed-list": [[String.raw`/\.k-[\w-]/`], {
      severity: "error",
      message: "A site styles its own className, and reaches the package through props, variants and tokens, not through its `.k-` classes.",
    }],
  },
  overrides: [
    {
      // The one sheet that names fixed colours. Each is a colour that holds in every theme (the
      // black of a QR code, the ink laid over cover art, the cue preview's sample frame), so the
      // other sheets read them as custom properties and carry no literal.
      files: ["src/fixed-colours.css"],
      rules: {
        "color-no-hex": null,
        "declaration-property-value-disallowed-list": null,
      },
    },
  ],
};
