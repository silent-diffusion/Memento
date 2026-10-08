import { describe, expect, it } from 'vitest';
import { filterByName, foldName, hasExactName, matchRank } from './speakerSearch';

const names = ['Sam Okafor', 'Aiko Tanaka', 'Lena Fischer', 'Speaker 4', 'Zoë Ångström', 'Ana-Sofía Ruiz', 'Samira Haddad'];
const pick = (query: string): string[] => filterByName(names, query, (n) => n);

describe('speaker search', () => {
  it('folds case, accents and spacing', () => {
    expect(foldName('  Zoë  Ångström ')).toBe('zoe angstrom');
    expect(foldName('ANA-SOFÍA')).toBe('ana-sofia');
  });

  it('lists every speaker in order for an empty query', () => {
    expect(pick('')).toEqual(names);
    expect(pick('   ')).toEqual(names);
  });

  it('puts names that start with the query first, then word starts, then anywhere', () => {
    expect(pick('sa')).toEqual(['Sam Okafor', 'Samira Haddad']);
    expect(pick('a')).toEqual(['Aiko Tanaka', 'Ana-Sofía Ruiz', 'Zoë Ångström', 'Sam Okafor', 'Lena Fischer', 'Speaker 4', 'Samira Haddad']);
    expect(pick('fisch')).toEqual(['Lena Fischer']);
    expect(pick('ische')).toEqual(['Lena Fischer']);
    expect(matchRank('Lena Fischer', 'len')).toBe(0);
    expect(matchRank('Lena Fischer', 'fi')).toBe(1);
    expect(matchRank('Lena Fischer', 'sch')).toBe(2);
    expect(matchRank('Lena Fischer', 'xyz')).toBeNull();
  });

  it('ignores accents and case both ways', () => {
    expect(pick('ZOE')).toEqual(['Zoë Ångström']);
    expect(pick('angstr')).toEqual(['Zoë Ångström']);
    expect(pick('sofia')).toEqual(['Ana-Sofía Ruiz']);
  });

  it('knows an exact name so Add is not offered twice', () => {
    expect(hasExactName(names, ' lena  fischer ', (n) => n)).toBe(true);
    expect(hasExactName(names, 'zoe angstrom', (n) => n)).toBe(true);
    expect(hasExactName(names, 'Lena', (n) => n)).toBe(false);
    expect(hasExactName(names, '', (n) => n)).toBe(false);
  });
});
