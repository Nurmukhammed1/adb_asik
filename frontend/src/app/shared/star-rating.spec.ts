import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_ICON_DEFAULT_OPTIONS } from '@angular/material/icon';
import { StarRating } from './star-rating';

describe('StarRating', () => {
  let fixture: ComponentFixture<StarRating>;

  async function render(inputs: { value?: number | null; readonly?: boolean }) {
    TestBed.configureTestingModule({
      providers: [{ provide: MAT_ICON_DEFAULT_OPTIONS, useValue: { fontSet: 'material-symbols-outlined' } }],
    });
    fixture = TestBed.createComponent(StarRating);
    for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
    await fixture.whenStable();
  }

  /** Each star as "full", "half" or "empty". */
  function stars(): string[] {
    const icons = fixture.nativeElement.querySelectorAll('mat-icon') as NodeListOf<HTMLElement>;
    return [...icons].map((icon) => {
      if (!icon.classList.contains('filled')) return 'empty';
      return icon.textContent?.trim() === 'star_half' ? 'half' : 'full';
    });
  }

  function buttons(): HTMLButtonElement[] {
    return [...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
  }

  it('shows a rating in half-star steps', async () => {
    await render({ value: 3.5 });

    expect(stars()).toEqual(['full', 'full', 'full', 'half', 'empty']);
  });

  it('shows no filled stars without a rating', async () => {
    await render({ value: null });

    expect(stars()).toEqual(['empty', 'empty', 'empty', 'empty', 'empty']);
  });

  it('offers all ten ratings from 0.5 to 5', async () => {
    await render({ value: null });

    expect(buttons().map((b) => b.getAttribute('aria-label'))).toEqual(
      [0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 4.5, 5].map((rating) => `Rate ${rating} out of 5`),
    );
  });

  it('reports the half or whole star that was clicked', async () => {
    await render({ value: null });
    const picked: number[] = [];
    fixture.componentInstance.rated.subscribe((rating) => picked.push(rating));

    buttons()[2].click(); // left half of the second star
    buttons()[9].click(); // right half of the fifth star

    expect(picked).toEqual([1.5, 5]);
  });

  it('previews the rating under the pointer and goes back when the pointer leaves', async () => {
    await render({ value: 1 });

    buttons()[7].dispatchEvent(new Event('mouseenter')); // right half of the fourth star
    await fixture.whenStable();
    expect(stars()).toEqual(['full', 'full', 'full', 'full', 'empty']);

    fixture.nativeElement.querySelector('.stars').dispatchEvent(new Event('mouseleave'));
    await fixture.whenStable();
    expect(stars()).toEqual(['full', 'empty', 'empty', 'empty', 'empty']);
  });

  it('cannot be clicked when read-only', async () => {
    await render({ value: 4, readonly: true });

    expect(buttons()).toHaveLength(0);
    expect(stars()).toEqual(['full', 'full', 'full', 'full', 'empty']);
  });
});
