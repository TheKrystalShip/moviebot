// How many cards each shelf shows (krystal's Rail `perView`), by the shelf's own width: each count holds
// from its width up. Fractional on purpose: the card cut off at the right edge is what says there is more.
// A film card stays at about 190px or wider: at 165px its length and a "1h 50m left" beside Resume are
// clipped.
export const POSTERS = { 0: 1.7, 431: 2.1, 541: 2.6, 701: 3.4, 901: 4.3, 1101: 5.2, 1351: 6.2 };
export const WIDE = { 0: 1.15, 431: 1.6, 761: 2.4, 1101: 3.4 };
export const TILES = { 0: 1.4, 431: 2.2, 761: 3.2, 1101: 4.3 };
