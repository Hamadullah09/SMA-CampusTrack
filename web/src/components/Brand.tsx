/**
 * The product's identity, in one place.
 *
 * The name and the mark appear in the sidebar, on the sign-in screen and in the loading
 * state. Keeping them here means a rebrand is one edit rather than a search across the app,
 * and it keeps the alt text consistent wherever the logo lands.
 */

export const PRODUCT_NAME = 'SMA Campus Track';

/** Square icon: the figure only. The wordmark is illegible below about 120px. */
export const BRAND_MARK = '/brand/sma-mark.png';

/** Full lock-up including "SMA Technology". Use where there is room for it to breathe. */
export const BRAND_LOGO = '/brand/sma-logo.png';

export function BrandMark({
  size = 34,
  className,
}: {
  size?: number;
  className?: string;
}) {
  return (
    <img
      src={BRAND_MARK}
      // Decorative wherever the product name is already written beside it; callers that
      // show the mark alone pass their own label.
      alt=""
      width={size}
      height={size}
      className={className}
      style={{ objectFit: 'contain' }}
    />
  );
}
