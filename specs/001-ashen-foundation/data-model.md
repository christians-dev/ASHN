# Data Model: Ashen Storefront Foundation

Conventions (all modules):

- **IDs**: `Guid` v7. References to another module's data are by ID only, with no foreign key
  across schemas.
- **Money**: a `Money` value object (`decimal Amount`, `string Currency` in ISO 4217). It is
  mapped as an EF Core complex type into two columns, `*_amount numeric(12,2)` and
  `*_currency char(3)`. Amounts are rounded half-up to the cent at line and order totals.
- **Timestamps**: `DateTimeOffset` in UTC (`timestamptz`), taken from `TimeProvider`.
- **Concurrency**: aggregates marked ⚡ use PostgreSQL `xmin` as the concurrency token.
- **Aggregate roots** are in **bold**. Children change only through methods on their root.
- **Audit**: every module schema has an `audit_entries` table: id, actor_user_id, action,
  entity_type, entity_id, details (jsonb), at. Admin slices write to it (FR-047).

---

## Identity (`identity` schema)

Built on ASP.NET Core Identity tables (`users`, `roles`, `user_roles`, `user_logins`,
`user_tokens`, …).

| Entity | Fields | Rules |
|---|---|---|
| **Account** (Identity user) | Id, Email (unique, normalized), DisplayName, PasswordHash?, LockoutEnd, AccessFailedCount, Status (Active, Deleted), CreatedAt | Password ≥ 8 chars and not in the breached list (FR-010). Lockout after 5 failures for 15 min (FR-011). A Google login is stored in `user_logins`; it links to an existing account only if Google says the email is verified (FR-008). |
| Role | Customer, Admin | Every new account gets Customer (FR-012). You cannot revoke Admin from the last Admin (FR-014). |

States: `Active → Deleted`. On deletion, email and name are replaced with anonymous values,
logins are removed, and `AccountDeletionRequested` is published.

## Customers (`customers` schema)

| Entity | Fields | Rules |
|---|---|---|
| **CustomerProfile** | Id (= Account Id), DisplayName, CreatedAt | Created the first time a signed-in customer needs it. |
| Address (child) | Id, RecipientName, Line1, Line2?, City, StateCode (2 letters), PostalCode, CountryCode, Phone, IsDefault | All fields except Line2 are required. Country must be one of the region's shipping countries (US). US states are validated against a list; the ZIP must be `^\d{5}(-\d{4})?$` (FR-017). Exactly one address is default if any exist, and the first address becomes default. |
| WishlistItem (child) | ProductId, AddedAt | Unique per (profile, product) (FR-041). |

## Catalog (`catalog` schema)

| Entity | Fields | Rules |
|---|---|---|
| Category | Id, Name (unique), Slug (unique), SortOrder | Can be deleted only when empty (FR-042). |
| **Product** ⚡ | Id, Name, Slug (unique), Description, CategoryId, Status, IsFeatured, FeaturedPosition?, HasBeenOrdered, Options (jsonb: 1–3 `{ name, values[] }`, e.g. `[{"name":"Size","values":["S","M","L"]},{"name":"Colour","values":["Black"]}]`), CreatedAt, PublishedAt? | Option names are unique per product (case-insensitive); values are unique within an option. Options can be added or removed only while the product has no variants; values can be added any time, but a value used by a variant cannot be removed. | Publishing requires ≥ 1 image and ≥ 1 variant. Name is 1–120 characters. HasBeenOrdered is set when stock is confirmed for an order; such products can only be archived, not deleted (FR-044). |
| ProductImage (child) | Id, StoragePath, AltText, SortOrder | JPEG, PNG or WebP, checked by file signature, ≤ 10 MB (FR-043). |
| Variant (child) ⚡ | Id, Sku (unique), OptionValues (jsonb, one value per product option, e.g. `{"Size":"M","Colour":"Black"}`), OptionKey (normalized `size=m;colour=black`, unique per product), Price (Money), StockOnHand (int ≥ 0), StockReserved (int ≥ 0) | Every product option has exactly one value, taken from that option's allowed values; no two variants of a product share an OptionKey. | Available = StockOnHand − StockReserved. A variant with Available ≤ 0 is unavailable (FR-004). |
| StockReservation | Id, CheckoutId, VariantId, Quantity, Status, ExpiresAt | Created in the same transaction as `StockReserved += qty`, and only if Available ≥ qty. |

Product status:

```text
Draft ──publish──► Published ──unpublish──► Unpublished ──publish──► Published
  any ──archive──► Archived   (used instead of delete once the product has been ordered, FR-044)
```

StockReservation status:

```text
Pending ──confirm──► Confirmed   (StockOnHand −= qty, StockReserved −= qty)
Pending ──release/expire──► Released   (StockReserved −= qty)
```

Cancelling an order adds the quantity back to StockOnHand through `Catalog.RestockForCancelledOrder`.

Search uses a trigram GIN index on `(name || ' ' || description)`, matched with ILIKE. Size and colour filters match the standard option names "Size" and "Colour" (case-insensitive) in variant `OptionValues`; other option names are not filterable in this phase. Filters run on
category, the variant `OptionValues` jsonb (Size, Colour) and price.

## Pricing (`pricing` schema)

| Entity | Fields | Rules |
|---|---|---|
| RegionSetting | Id, CountryCode (US), CurrencyCode (USD), Locale (en-US), ShippingCountries (text[]), Subdivisions (jsonb: states), PostalCodePattern (regex, US: `^\d{5}(-\d{4})?$`), IsActive | Exactly one is active (FR-033). |
| TaxRate | Id, RegionId, JurisdictionCode (e.g. `IL`), Rate (numeric(6,5), e.g. 0.06250), EffectiveFrom | The rate used is the latest whose EffectiveFrom ≤ now. If no rate exists, it is 0 (FR-030). Seeded with `IL` = 0.0625 (FR-031). |
| StoreSettings | Id, FlatShippingFee (Money) | One row (FR-024). |

Quote calculation: `tax = round_half_up((subtotal − discount) × rate, 2)`. Shipping is not taxed.

## Promotions (`promotions` schema)

| Entity | Fields | Rules |
|---|---|---|
| **Coupon** ⚡ | Id, Code (unique, case-insensitive), DiscountType (Percentage, FixedAmount), Value, MinimumSpend (Money?), StartsAt, EndsAt?, TotalUsageLimit?, OnePerCustomer, IsActive, RedemptionCount | Percentage is between 0 and 100. Discount = min(computed, subtotal) (FR-038). Valid only if active, inside its date range, under its usage limit, and the minimum spend is met. |
| CouponRedemption | Id, CouponId, CustomerId, CheckoutId, OrderId?, Status (Pending, Confirmed, Released), ExpiresAt | Pending and Confirmed redemptions count toward the limits. Reserving one increments RedemptionCount under the ⚡ concurrency check. |

## Ordering (`ordering` schema)

| Entity | Fields | Rules |
|---|---|---|
| **Cart** | Id, CustomerId? , AnonymousToken?, CouponCode?, UpdatedAt | Belongs to either a customer or an anonymous cookie token. When a signed-in customer's request still carries the guest `ashen_cart` cookie, Ordering merges the guest cart into theirs (matching variants have their quantities added together, capped at 99) and deletes the cookie (FR-018). |
| CartItem (child) | VariantId, Quantity (1–99) | Unique per variant. |
| **Order** | Id, OrderNumber (`ASH-` + 6-digit sequence), CustomerId, CheckoutId (unique; = the client's checkout idempotency key), Status, PlacedAt, Subtotal, Discount, Shipping, Tax, TaxRate, TaxJurisdiction, Total (all Money), CouponCode?, ShipTo (jsonb address snapshot), PaymentSummary (brand, last4), PaymentId | Total = Subtotal − Discount + Shipping + Tax. Every snapshot field is immutable (FR-022). |
| OrderLine (child) | ProductId, VariantId, ProductName, Sku, OptionValues (jsonb), UnitPrice (Money), Quantity, LineTotal | Copied from Catalog at the time of purchase. |

Order status:

```text
Paid ──ship──► Shipped ──deliver──► Delivered
Paid ──cancel──► Cancelled   (stock restored, full simulated refund, FR-036)
             by an admin any time while Paid; by the customer only within 1 hour of PlacedAt (FR-035)
```

Order is ⚡ (`xmin`), so a customer cancel and an admin ship racing on the same order cannot both succeed; the loser gets a conflict.

Order status changes publish `OrderShipped` (→ email), `OrderDelivered` and `OrderCancelled`.

## Payments (`payments` schema)

| Entity | Fields | Rules |
|---|---|---|
| SavedCard | Id, CustomerId, GatewayToken, Brand, Last4, ExpMonth, ExpYear, IsDefault, CreatedAt | No card number or CVC is ever stored (FR-027). A card past its expiry date cannot be chosen (Story 8). |
| **Payment** | Id, CheckoutId, OrderId?, CustomerId, Amount (Money), GatewayReference, Status, CreatedAt, UpdatedAt | |

Payment status:

```text
Authorized ──capture──► Captured ──refund──► Refunded
Authorized ──void──► Voided   (compensation, or after 15 min with no order — StaleAuthorizationVoider, FR-021)
Declined   (terminal)
```

## Reviews (`reviews` schema)

| Entity | Fields | Rules |
|---|---|---|
| **Review** | Id, ProductId, CustomerId, AuthorDisplayName, Rating (1–5), Text? (≤ 2,000 characters), IsHidden, CreatedAt, UpdatedAt | Unique per (product, customer). Creating one requires `Ordering.HasDeliveredProduct` to be true (FR-039). Hidden reviews are excluded from the average (FR-040). When the author's account is deleted, AuthorDisplayName becomes "Former customer". |

The product rating summary (average and count) is computed by a query over visible reviews and
exposed through `IReviewsModule`.

## Cross-module ID references

| From | To | Field |
|---|---|---|
| Customers.WishlistItem | Catalog.Product | ProductId |
| Ordering.CartItem / OrderLine | Catalog.Variant / Product | VariantId, ProductId |
| Ordering.Order, Cart; Payments.*; Customers.Profile; Reviews.Review | Identity.Account | CustomerId |
| Ordering.Order | Payments.Payment | PaymentId |
| Reviews.Review | Catalog.Product | ProductId |
