/**
 * A query string from named values: a list repeats its key once per value (`decade=1990&decade=2020`),
 * and an empty value or list leaves its key out.
 */
export function queryOf(params) {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params || {})) {
    for (const one of Array.isArray(value) ? value : [value]) {
      if (one !== null && one !== undefined && one !== "") query.append(key, String(one));
    }
  }
  return query.toString();
}
