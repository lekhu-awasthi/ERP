import { toAdDisplay, toBillDisplay, toBsDisplay} from './nepali-date-pipe';
describe('nepali-date-pipe display functions', () => {
    it('renders an AD date as dd-MM-yyyy', () => {
        expect(toAdDisplay('2026-09-01')).toBe('01-09-2026');
    });
    it('converts to BS and reorders', () => {
        expect(toBsDisplay('2026-09-01')).toBe('16-05-2083');
    });
    it('puts BS over AD on a bill', () => {
        expect(toBillDisplay('2026-09-01')).toBe('16-05-2083\u00a0BS\n(01-09-2026\u00a0AD)');
    });
    it('drops the BS half for a date outside the table', () => {
        expect(toBillDisplay('2040-01-01')).toBe('01-01-2040\u00a0AD');
    });
});