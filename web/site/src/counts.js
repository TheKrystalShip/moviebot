/** A count with its noun, as a shelf's line under its heading: "1 title", "28 titles", "40+ titles". */
export const counted = (n, one, many = one + "s") => `${n} ${n === 1 ? one : many}`;
