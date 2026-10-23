// ESLint flat config. Narrow on purpose: the static bug classes a build passes silently, undeclared
// identifiers and Rules-of-Hooks violations, and the design system's rules are errors; the rest are
// warnings.
import js from "@eslint/js";
import globals from "globals";
import reactHooks from "eslint-plugin-react-hooks";
import react from "eslint-plugin-react";
import { configs as krystal } from "@thekrystalship/krystal/eslint";

export default [
  { ignores: ["dist/**", "node_modules/**"] },

  js.configs.recommended,

  // The player's engine is TypeScript, checked by `tsc`; the design-system rules read the JavaScript.
  ...krystal.strict.map((entry) => ({ ...entry, files: ["**/*.{js,jsx,mjs}"] })),

  {
    files: ["**/*.{js,jsx,mjs}"],
    languageOptions: {
      ecmaVersion: 2023,
      sourceType: "module",
      parserOptions: { ecmaFeatures: { jsx: true } },
      globals: { ...globals.browser, ...globals.es2023 },
    },
    plugins: { "react-hooks": reactHooks, react },
    settings: { react: { version: "18.3" } },
    rules: {
      "react/jsx-uses-vars": "error",
      "react/jsx-no-undef": "error",
      "no-undef": "error",
      "react-hooks/rules-of-hooks": "error",
      "react-hooks/exhaustive-deps": "warn",
      "no-unused-vars": ["warn", { args: "none", ignoreRestSiblings: true, varsIgnorePattern: "^_" }],
      "no-empty": ["warn", { allowEmptyCatch: true }],
    },
  },

  {
    // The checks under verify/ run in Node and drive a browser, so they see both; scripts/ are the
    // authoring tools, run in Node.
    files: ["vite.config.*", "eslint.config.js", "identity.js", "verify/**/*.mjs"],
    languageOptions: { globals: { ...globals.node } },
  },

  {
    files: ["public/sw.js"],
    languageOptions: { sourceType: "script", globals: { ...globals.serviceworker } },
  },
];
