# Feature Specification: Ashen Storefront Foundation

**Feature Branch**: `001-ashen-foundation`

**Created**: 2026-11-03

**Status**: Draft

**Input**: User description: "Planning an apparel app called Ashen. Create the entire repo and foundational architecture. Customers can sign in (including with Google), add a payment method and an address, and browse/shop products (apparel, but not limited to clothing). Admins (shop owners) sign in with different access and manage products. Two roles: customer and admin. Design to be discussed later."

## Clarifications

### Session 2026-11-03

- Q: Should payments be real, simulated, or absent in phase 1? → A: No real payments in phase 1. Payments are simulated inside the store by a stand-in "Simulated Payment Gateway", built so a real provider can replace it later without changing checkout behaviour.
- Q: Is buying (cart, checkout, orders) part of phase 1? → A: Yes. Cart, checkout, and order history are all in scope.
- Q: Which market? → A: United States only, prices in US dollars, with sales tax. Only Illinois tax is configured at launch, but tax rules and regional settings must be extendable to other states and, later, other countries.
- Q: Discounts, reviews, wishlists? → A: All three are in scope: discount coupons, product reviews, and wishlists. A home page that showcases products is in scope; its visual design is deferred.
- Q: How are variant options defined? → A: Each product defines up to 3 named options (e.g. Size, Colour, Width) with their allowed values; every variant picks one value per option. "Size" and "Colour" are standard option names that power the storefront filters.
- Q: When is the card charged? → A: Immediately at order placement: the payment is authorised and captured as the order is created, so every order starts as Paid, and cancelling a Paid order issues a full refund. Capture-at-shipment may be revisited when a real payment provider is introduced.
- Q: Can customers cancel their own orders? → A: Yes, within 1 hour of placing the order and only while it is still Paid (not yet Shipped). After that only admins can cancel. A customer cancellation restores stock and issues a full refund, the same as an admin cancellation.
- Q: When is stock held for a shopper? → A: Only when the order is placed. Carts hold no stock; cart and checkout show current availability, and an item that has sold out blocks checkout with a clear message.

## Overview

Ashen is an online apparel store run by a single shop owner (or a small team of owners), selling in the United States. Shoppers browse a catalog of products, sign in to an account, save delivery addresses and payment cards, keep a wishlist, buy products with optional discount coupons, and review what they bought. Shop owners sign in as administrators to run the store: maintain the catalog and stock, feature products on the home page, configure tax and coupons, moderate reviews, and fulfil orders.

This first feature establishes the whole store end to end at a basic level. In this phase no real money moves: payment is simulated. Visual design (branding, look and feel) is explicitly deferred; this spec defines *what* the store does, not how it looks.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Browse the catalog (Priority: P1)

A visitor, signed in or not, lands on the home page, sees featured products and categories, lists products in a category, searches by name, filters by size, colour and price, and opens a product to see its images, description, price, average rating, and which sizes/colours are in stock.

Average ratings and sorting by rating appear once product reviews (User Story 10) exist; until then products show no rating.

**Why this priority**: Browsing is the core of any store. Without it nothing else has value, and it is demonstrable with a seeded catalog alone.

**Independent Test**: Seed a catalog of products and confirm a visitor can find a specific product by category, by search, and by filter, and see its available sizes and colours.

**Acceptance Scenarios**:

1. **Given** the catalog contains published products in the "Jackets" category, **When** a visitor opens that category, **Then** they see every published jacket with name, primary image, and price.
2. **Given** a product has sizes S, M, L and size L is out of stock, **When** a visitor opens the product, **Then** S and M are selectable and L is shown as unavailable.
3. **Given** a visitor searches for "hoodie", **When** results load, **Then** only published products whose name or description matches are shown.
4. **Given** an admin has unpublished a product, **When** a visitor browses or searches, **Then** that product does not appear and its direct link shows a "not available" page.
5. **Given** an admin has marked 4 products as featured, **When** a visitor opens the home page, **Then** those 4 published products are shown in the admin's chosen order.

---

### User Story 2 - Create an account and sign in (Priority: P2)

A shopper creates an account with email and password, or signs in with their Google account. They can sign out, and reset a forgotten password by email.

**Why this priority**: Accounts are required for saving addresses, payment methods, wishlists and orders, and for separating customers from admins.

**Independent Test**: Register with email/password, sign out, sign back in; separately sign in with a Google account; confirm both land on a signed-in state showing the shopper's name.

**Acceptance Scenarios**:

1. **Given** no account exists for an email, **When** a shopper registers with that email and a valid password, **Then** an account is created with the Customer role and they are signed in.
2. **Given** a shopper has a Google account, **When** they choose "Continue with Google" and approve, **Then** they are signed in; an account is created on first use.
3. **Given** an account already exists for an email via password registration, **When** the same person signs in with Google using that verified email, **Then** they reach the same account rather than a duplicate.
4. **Given** a shopper forgot their password, **When** they request a reset, **Then** they receive an email link that lets them set a new password once, and expires after 1 hour.
5. **Given** 5 consecutive failed sign-in attempts on one account, **When** a sixth attempt is made within 15 minutes, **Then** it is refused with a message to try again later.

---

### User Story 3 - Admin manages the catalog (Priority: P3)

An admin signs in and creates, edits, publishes and unpublishes products. Each product has a name, description, category, images, and one or more variants (for example size and colour combinations), each with its own price and stock quantity. Admins also manage categories and choose which products are featured on the home page.

**Why this priority**: The store cannot operate without a way for owners to put products on sale. It is the second half of the core loop with browsing.

**Independent Test**: Sign in as an admin, create a product with three size variants, publish it, and confirm it appears to a visitor (Story 1); confirm a customer account cannot reach any admin screen.

**Acceptance Scenarios**:

1. **Given** a signed-in admin, **When** they create a product with name, category, description, at least one image, and at least one variant with price and stock, **Then** the product is saved as a draft and not visible to shoppers.
2. **Given** a draft product, **When** the admin publishes it, **Then** it becomes visible in the catalog.
3. **Given** a signed-in customer, **When** they attempt to open any admin page or admin action, **Then** access is refused.
4. **Given** an admin sets a variant's stock to 0, **When** a shopper views the product, **Then** that variant is shown as unavailable.
5. **Given** a product has appeared on past orders, **When** an admin tries to delete it, **Then** the system archives it instead so order history stays intact.

---

### User Story 4 - Manage delivery addresses (Priority: P4)

A signed-in customer adds, edits, and removes US delivery addresses in their account and marks one as default.

**Why this priority**: Needed before checkout (the address also decides sales tax), but small and independently useful.

**Independent Test**: Add two addresses, set the second as default, delete the first, and confirm the remaining address is default.

**Acceptance Scenarios**:

1. **Given** a signed-in customer, **When** they add an address with recipient name, street, city, state, ZIP code and phone, **Then** it is saved to their account.
2. **Given** a customer has no addresses, **When** they add their first address, **Then** it becomes the default.
3. **Given** a required field is missing or the ZIP code is not a valid US format (5 digits or ZIP+4), **When** they save, **Then** the field is highlighted with a clear message and nothing is saved.
4. **Given** a customer enters an address outside the United States, **When** they save, **Then** it is refused with a message that the store currently ships to the US only.

---

### User Story 5 - Cart and checkout with simulated payment (Priority: P5)

A shopper adds variants to a cart, changes quantities, and checks out by choosing a delivery address and a payment card. The store shows subtotal, discount, shipping, sales tax, and total before the order is placed. Payment is handled by the Simulated Payment Gateway, which approves or declines without moving real money. On approval an order is created, stock is reduced, and the shopper sees a confirmation and receives a confirmation email.

**Why this priority**: Completes the purchase loop; depends on Stories 1, 2 and 4.

**Independent Test**: With a seeded catalog, a customer adds two variants to the cart, checks out with a simulated card that is set to approve, and sees an order confirmation; stock for each variant drops by the quantity bought. Repeat with a card set to decline and confirm no order is created.

**Acceptance Scenarios**:

1. **Given** a visitor who is not signed in adds items to a cart, **When** they sign in, **Then** the cart items are kept and merged into their account cart.
2. **Given** a cart with quantity 3 of a variant that has 2 in stock, **When** the shopper tries to check out, **Then** they are told only 2 are available and can adjust before paying.
3. **Given** a valid cart, address and an approving simulated card, **When** the shopper places the order, **Then** an order is created with status "Paid", stock is reduced, and a confirmation is shown and emailed.
4. **Given** a declining simulated card, **When** the shopper places the order, **Then** no order is created, stock is unchanged, the cart is kept, and a clear "payment declined" message is shown.
5. **Given** a product's price changes after an order is placed, **When** the customer views that order, **Then** it still shows the price paid.
6. **Given** the store is in simulated-payment mode, **When** a shopper reaches checkout, **Then** the page clearly states that no real payment will be taken.

---

### User Story 6 - Sales tax and regional settings (Priority: P6)

Sales tax is calculated at checkout from the delivery address's state using tax rates the admin configures per state. At launch only Illinois has a rate. Store-wide regional settings (country, currency, and how money and dates are displayed) are defined once and used everywhere.

**Why this priority**: US sales must charge the correct tax; setting it up as configurable rules now avoids a rewrite when more states or countries are added.

**Independent Test**: With Illinois configured at 6.25%, check out a $100.00 cart to an Illinois address and confirm $6.25 tax; check out the same cart to a Texas address and confirm $0.00 tax.

**Acceptance Scenarios**:

1. **Given** Illinois is configured at 6.25% and a cart subtotal of $100.00 after discounts, **When** the shopper selects an Illinois address, **Then** sales tax of $6.25 is shown and added to the total.
2. **Given** no rate is configured for Texas, **When** the shopper selects a Texas address, **Then** sales tax is $0.00.
3. **Given** an admin changes the Illinois rate, **When** a new checkout starts, **Then** the new rate applies; past orders keep the tax they were charged.
4. **Given** a tax amount with fractions of a cent, **When** it is calculated, **Then** it is rounded to the nearest cent (half up) on the order total.
5. **Given** the store's region is United States, **When** any price is displayed, **Then** it is shown in US dollars in US format (e.g. $1,234.50).

---

### User Story 7 - Order history and fulfilment (Priority: P7)

Customers see their past orders and each order's status, and can cancel an order themselves within 1 hour of placing it while it is still Paid. Admins see all orders, filter by status, and move an order through Paid → Shipped → Delivered, or cancel it (which restores stock and records a simulated refund).

**Why this priority**: Needed to run the store day to day once buying exists.

**Independent Test**: Place an order, have the admin mark it shipped, and confirm the customer sees "Shipped" on their order.

**Acceptance Scenarios**:

1. **Given** a customer with two orders, **When** they open order history, **Then** they see both, newest first, with date, total and status.
2. **Given** an admin views a Paid order, **When** they mark it Shipped, **Then** the customer sees status "Shipped" and receives a shipping email.
3. **Given** an admin cancels a Paid order, **When** cancellation completes, **Then** stock is restored, a simulated refund of the full amount is recorded, and the order shows "Cancelled".
4. **Given** a customer, **When** they try to open another customer's order, **Then** access is refused.
5. **Given** a customer placed an order 30 minutes ago and it is still Paid, **When** they cancel it, **Then** it shows "Cancelled", stock is restored, and a full simulated refund is recorded.
6. **Given** an order placed more than 1 hour ago, or already Shipped, **When** the customer tries to cancel it, **Then** the cancel option is not offered and a direct request is refused with a message to contact the store.

---

### User Story 8 - Saved payment cards (Priority: P8)

A signed-in customer saves simulated payment cards to their account, sees saved cards (brand, last four digits, expiry only), sets a default, and removes cards. At checkout they pick a saved card or enter a new one.

**Why this priority**: Makes repeat checkout faster; checkout already works with a card entered at the time.

**Independent Test**: Save a card, confirm only brand/last four/expiry are displayed, remove it, and confirm it no longer appears.

**Acceptance Scenarios**:

1. **Given** a signed-in customer, **When** they add a card the Simulated Payment Gateway accepts, **Then** it appears in their saved cards showing brand, last four digits and expiry only.
2. **Given** a card the Simulated Payment Gateway rejects, **When** they submit, **Then** a clear error is shown and nothing is saved.
3. **Given** a saved card has expired, **When** the customer views their cards, **Then** it is marked expired and cannot be chosen at checkout.

---

### User Story 9 - Discount coupons (Priority: P9)

Admins create coupon codes that take a percentage or a fixed amount off the cart. Shoppers enter a code at checkout and see the discount applied.

**Why this priority**: Common sales tool; checkout works without it.

**Independent Test**: Create a 10%-off code, apply it to a $50.00 cart, and confirm a $5.00 discount; confirm an expired code is refused.

**Acceptance Scenarios**:

1. **Given** an active coupon "WELCOME10" for 10% off, **When** a shopper applies it to a $50.00 cart, **Then** a $5.00 discount is shown and the total is recalculated (tax is calculated after the discount).
2. **Given** a coupon with a $75.00 minimum spend, **When** applied to a $60.00 cart, **Then** it is refused with a message stating the minimum.
3. **Given** a coupon past its end date, has reached its total usage limit, or was already used by this customer when limited to one use per customer, **When** applied, **Then** it is refused with the reason.
4. **Given** a fixed $20.00 coupon on a $15.00 cart, **When** applied, **Then** the discount is capped at $15.00 and the merchandise total never goes below $0.00.
5. **Given** a coupon has been applied, **When** the shopper tries a second coupon, **Then** it replaces the first (one coupon per order).

---

### User Story 10 - Product reviews (Priority: P10)

Customers who have received a product rate it 1-5 stars and write a review. Shoppers see reviews and the average rating on the product page. Admins can hide inappropriate reviews.

**Why this priority**: Builds trust for shoppers; not needed to buy.

**Independent Test**: As a customer with a delivered order for a product, post a 4-star review; confirm it appears on the product page and the average updates; confirm a customer who never bought it cannot review.

**Acceptance Scenarios**:

1. **Given** a customer with a Delivered order containing a product, **When** they submit a rating and review text, **Then** it appears on the product page with their display name and date.
2. **Given** a customer who has not received the product, **When** they try to review it, **Then** they are told only verified buyers can review.
3. **Given** a customer has already reviewed a product, **When** they review again, **Then** their existing review is updated instead of a second one added.
4. **Given** an admin hides a review, **When** shoppers view the product, **Then** the review is not shown and does not count toward the average.

---

### User Story 11 - Wishlist (Priority: P11)

A signed-in customer saves products to a wishlist, views it, removes items, and moves an item to the cart by choosing a size/colour.

**Why this priority**: Convenience feature; least critical to running the store.

**Independent Test**: Add two products to the wishlist, remove one, move the other into the cart, and confirm the cart contains it.

**Acceptance Scenarios**:

1. **Given** a signed-in customer on a product page, **When** they add it to their wishlist, **Then** it appears in their wishlist.
2. **Given** a visitor who is not signed in, **When** they try to add to a wishlist, **Then** they are asked to sign in first.
3. **Given** a wishlisted product is later unpublished, **When** the customer opens their wishlist, **Then** it is shown as no longer available and cannot be moved to the cart.

---

### User Story 12 - Admin manages admins (Priority: P12)

The first admin exists from store setup. Admins can grant the admin role to an existing customer account and revoke it.

**Why this priority**: Only matters once more than one owner runs the store.

**Independent Test**: As the initial admin, promote a customer to admin; that person signs in and can reach admin screens; revoke and confirm they no longer can.

**Acceptance Scenarios**:

1. **Given** store setup is complete, **When** the configured initial admin signs in, **Then** they have admin access.
2. **Given** only one admin remains, **When** they try to revoke their own admin role, **Then** it is refused.

---

### Edge Cases

- A customer cancels while an admin marks the same order Shipped at the same moment: exactly one change wins; if shipping wins, the customer is told the order has already shipped.
- Two shoppers check out the last unit of a variant at the same moment: exactly one succeeds; the other is told it sold out and is not charged.
- A product is unpublished while in someone's cart: it is flagged in the cart and cannot be checked out.
- A variant's price changes while in a cart: the cart shows the current price before payment.
- The shopper changes the delivery address at checkout from an Illinois address to an out-of-state one: tax is recalculated before payment.
- A coupon expires between applying it and placing the order: the order is not placed and the shopper is told the coupon is no longer valid.
- A coupon with a usage limit of 1 is redeemed by two shoppers at the same moment: only one order gets the discount.
- Payment is approved but the order cannot be saved: the simulated payment is reversed so no customer is charged without an order.
- A shopper signs in with Google whose email is not verified by Google: no automatic linking to an existing account.
- A customer deletes an address or card that was used on a past order: past orders still show the details captured at the time.
- An admin uploads an image in an unsupported format or larger than 10 MB: rejected with a clear message.
- A customer requests account deletion: personal data is removed or anonymised; order records needed for accounting are kept without personal identifiers; their reviews show "Former customer".
- The session expires mid-checkout: the cart is kept and the shopper resumes after signing in again.

## Requirements *(mandatory)*

### Functional Requirements

**Catalog & browsing**

- **FR-001**: System MUST let anyone, signed in or not, browse published products by category, view product details, and search by text.
- **FR-002**: System MUST let shoppers filter product lists by category, size, colour, and price range (size and colour filters use the standard option names "Size" and "Colour"; other option names are not filterable in this phase), and sort by newest, price, and average rating (rating sort available once reviews exist, User Story 10).
- **FR-003**: Each product MUST have a name, description, category, one or more images, and one or more variants; each product defines 1–3 named options (e.g. Size, Colour, Width) with their allowed values, and each variant has exactly one value per option, plus its own SKU, price, and stock quantity. No two variants of a product may have the same combination of values.
- **FR-004**: System MUST show out-of-stock variants as unavailable and prevent adding them to a cart.
- **FR-005**: Products MUST NOT be limited to clothing; any apparel item (shoes, accessories, bags) can be modelled with the same product/variant structure.
- **FR-006**: System MUST provide a home page showing admin-selected featured products and the product categories.

**Accounts & access**

- **FR-007**: System MUST let shoppers register and sign in with email and password, and sign in with a Google account.
- **FR-008**: System MUST link a Google sign-in to an existing account with the same verified email rather than create a duplicate.
- **FR-009**: System MUST support password reset via a single-use emailed link valid for 1 hour.
- **FR-010**: System MUST require passwords of at least 8 characters and reject passwords known to be commonly breached.
- **FR-011**: System MUST temporarily lock sign-in for an account for 15 minutes after 5 consecutive failed password attempts.
- **FR-012**: System MUST have exactly two roles: Customer (default for every new account) and Admin.
- **FR-013**: System MUST refuse every admin page and admin action to non-admins, and refuse access to one customer's data (addresses, cards, wishlist, orders) by any other customer.
- **FR-014**: System MUST create the initial admin from store setup configuration, let admins grant and revoke the admin role, and prevent removal of the last admin.
- **FR-015**: Customers MUST be able to view and edit their name and email, and request deletion of their account.

**Addresses**

- **FR-016**: Customers MUST be able to add, edit, delete addresses and choose one default address.
- **FR-017**: Addresses MUST be validated against the rules of the store's region (for the US: a valid state and a 5-digit or ZIP+4 code); addresses outside the store's shipping countries MUST be refused.

**Cart & checkout**

- **FR-018**: System MUST let shoppers add variants to a cart, change quantities, and remove items; a guest cart MUST be kept when they sign in. Adding to a cart MUST NOT hold stock; stock is claimed only at the moment the order is placed.
- **FR-019**: Checkout MUST show subtotal, discount, shipping, sales tax and total before the order is placed, and MUST re-check stock, price, and coupon validity at the moment of placing it.
- **FR-020**: System MUST only create an order after payment is approved, MUST capture the full payment when the order is placed (orders start as Paid), and MUST reduce stock in the same step so no variant is sold beyond its stock.
- **FR-021**: If an order cannot be completed after payment approval, the payment MUST be reversed.
- **FR-022**: Each order MUST record the prices, product names, variant options, delivery address, discount, tax rate and amount, and payment summary as they were at purchase time.
- **FR-023**: System MUST email the customer an order confirmation and a notice when the order ships.
- **FR-024**: Shipping MUST be a single flat fee configured by the admin (which may be $0.00).

**Payments**

- **FR-025**: In this phase all payments, saved cards and refunds MUST be handled by a Simulated Payment Gateway that moves no real money and approves or declines based on documented test card numbers (at least: always approve, always decline, insufficient funds).
- **FR-026**: Checkout, saved cards, and refunds MUST depend only on a general payment-gateway capability (authorise, capture, refund, save card) so a real payment provider can replace the simulated one without changing how checkout behaves for shoppers.
- **FR-027**: System MUST NOT store full card numbers or security codes; only a reference from the payment gateway plus brand, last four digits and expiry are kept.
- **FR-028**: While the Simulated Payment Gateway is active, checkout and saved-card pages MUST state clearly that no real payment is taken.
- **FR-029**: Customers MUST be able to save, list, set default, and remove payment cards.

**Tax & regional settings**

- **FR-030**: System MUST calculate sales tax from the delivery address's state using admin-configured rates per state; states without a configured rate are charged $0.00 tax.
- **FR-031**: At launch the store MUST be configured for Illinois only, at the statewide rate of 6.25%, applied to merchandise after discounts and not to shipping.
- **FR-032**: Tax calculation MUST be a replaceable capability so per-city/county rates or an external tax service can be added later without changing checkout behaviour.
- **FR-033**: The store MUST have one active region setting holding country (United States), currency (US dollar), shipping countries, and display formats for money, dates and addresses; all prices, totals and dates MUST be displayed using it. Adding another region later MUST not require changing existing business rules.
- **FR-034**: All money amounts MUST be stored with their currency and rounded to the nearest cent (half up) at the order line and order total.

**Orders & fulfilment**

- **FR-035**: Customers MUST be able to see their own order history and each order's status, and cancel their own order within 1 hour of placement while it is Paid (restoring stock and issuing a full simulated refund); later cancellations are admin-only.
- **FR-036**: Admins MUST be able to list all orders, filter by status, and move orders through Paid → Shipped → Delivered, or Cancelled (restoring stock and issuing a full simulated refund).

**Discount coupons**

- **FR-037**: Admins MUST be able to create, edit, deactivate coupons with: code, percentage or fixed-amount discount, optional minimum spend, start and end dates, optional total usage limit, and optional one-use-per-customer rule.
- **FR-038**: Only one coupon MAY apply per order; the discount MUST never make the merchandise total negative, and tax MUST be calculated after the discount.

**Reviews**

- **FR-039**: Only customers with a Delivered order containing a product MAY review it; each customer has at most one review per product (1-5 stars plus optional text up to 2,000 characters), which they can edit or delete.
- **FR-040**: Product pages MUST show visible reviews and the average rating with review count; admins MUST be able to hide and unhide reviews.

**Wishlist**

- **FR-041**: Signed-in customers MUST be able to add products to and remove them from a single wishlist, and move an item to the cart by choosing a variant.

**Catalog administration**

- **FR-042**: Admins MUST be able to create, edit, publish, unpublish, and archive products, create, rename and remove empty categories, and select and order featured products.
- **FR-043**: Admins MUST be able to upload product images (JPEG, PNG or WebP, up to 10 MB each) and set their display order.
- **FR-044**: Products that appear on any order MUST be archived, never permanently deleted.
- **FR-045**: Admins MUST be able to set and adjust stock quantity per variant.

**Platform**

- **FR-046**: The store MUST be usable on current desktop and mobile web browsers.
- **FR-047**: System MUST record an audit trail of admin actions (who, what, when) for catalog, stock, coupon, tax rate, review moderation, role and order-status changes.
- **FR-048**: The store MUST be divided into business areas (Catalog, Identity, Customers, Ordering, Payments, Pricing & Tax, Promotions, Reviews) whose data and rules are owned by one area each, so any area could later be split out to run on its own without changing the others' behaviour.

### Key Entities

- **Account**: A person who can sign in. Email, display name, role (Customer or Admin), sign-in methods (password and/or Google), status (active, locked, deleted).
- **Customer Profile**: The shopping-side view of a customer account; owns their addresses and wishlist.
- **Address**: Recipient name, street lines, city, state, ZIP code, country, phone, default flag. Belongs to one customer.
- **Saved Card**: Reference from the payment gateway, card brand, last four digits, expiry, default flag. Belongs to one customer but is kept by the payments area, not the customer profile, so card data stays in one place. Never contains the full card number.
- **Category**: A named grouping of products (e.g. Jackets, Shoes). A product belongs to one category.
- **Product**: Name, description, category, images, status (draft, published, unpublished, archived), featured flag and position, and its option definitions (1–3 option names, each with an ordered list of allowed values). Owns its variants.
- **Variant**: One buyable version of a product: one value for each of the product's options (e.g. Size M, Colour Black), unique per product, SKU, price, stock quantity.
- **Product Image**: Image file, alt text, display order. Belongs to one product.
- **Cart**: Items a shopper intends to buy and an optional applied coupon; belongs to a guest session or a customer. Each **Cart Item** is a variant and a quantity.
- **Order**: A completed purchase. Customer, order number, status (Paid, Shipped, Delivered, Cancelled), placement date, subtotal, discount, shipping, tax, total, currency, snapshot of delivery address and payment summary. Owns its **Order Lines**.
- **Order Line**: Snapshot of product name, variant options, SKU, unit price paid, and quantity.
- **Payment**: One payment attempt for an order: amount, currency, gateway reference, status (approved, declined, refunded, reversed).
- **Region Setting**: Country, currency, shipping countries, display formats. One is active.
- **Tax Rate**: Jurisdiction (state), rate, effective from date. Belongs to a region.
- **Coupon**: Code, discount type and value, minimum spend, validity dates, usage limits, active flag; with a record of each redemption.
- **Review**: Customer, product, rating 1-5, text, date, visibility.
- **Wishlist Item**: Customer, product, date added.
- **Admin Audit Entry**: Who did what to which item, and when.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time visitor can find a specific product via the home page, category, search or filter and open it in under 1 minute.
- **SC-002**: A new shopper can create an account (email or Google) in under 1 minute.
- **SC-003**: A signed-in customer with a saved address and card can go from cart to order confirmation in under 2 minutes, including applying a coupon.
- **SC-004**: An admin can create and publish a new product with 3 variants and 2 images in under 5 minutes.
- **SC-005**: Product list and product pages appear within 2 seconds for 95% of views with a catalog of 1,000 products.
- **SC-006**: The store supports 200 shoppers browsing at the same time without visible slowdown.
- **SC-007**: In a test of 100 simultaneous attempts to buy the last unit of a variant, exactly 1 order is created and 0 customers are charged without an order.
- **SC-008**: 100% of attempts by a customer account to reach admin functions or another customer's data are refused in security testing.
- **SC-009**: No full card number or security code is retrievable from any part of the store's stored data.
- **SC-010**: For a test set of at least 20 checkouts covering Illinois and non-Illinois addresses, with and without coupons, 100% of order totals and tax amounts match a hand calculation to the cent.
- **SC-011**: Adding a tax rate for a second state is done by an admin through settings alone, with no change to the store's software.

## Assumptions

- Single store and brand (Ashen), one region (United States), US dollars, English only.
- Prices are shown before tax, as is normal in the US; tax is added at checkout.
- Only the Illinois statewide rate (6.25%) is charged in this phase. Illinois local (city/county) rates, tax-exempt customers, and per-category tax rules (e.g. reduced rates on some clothing) are out of scope; the store owner is responsible for confirming tax obligations with an adviser before taking real payments.
- Orders may ship to any US state; states without a configured rate are charged no tax.
- Shipping is one flat fee; carrier rates, tracking numbers and delivery estimates are out of scope.
- Shoppers can browse and fill a cart without an account, but must sign in to check out (no guest checkout).
- Google is the only social sign-in provider in this phase.
- Email verification is not required to shop, but the store sends transactional emails (password reset, order confirmation, shipping notice).
- Out of scope for this phase: real payment processing, returns and partial refunds, gift cards, multiple warehouses, marketing emails, product recommendations, native mobile apps, multiple languages or currencies.
- Visual design, branding, page layout and home page content beyond featured products will be decided in a later discussion; this phase may use a plain, accessible default look.
- This first feature also covers creating the repository and its foundational structure. Technology choices and engineering principles (front-end framework, back-end platform, database, hosting, SOLID/clean-code rules) are recorded in the project constitution and implementation plan, not in this spec.
