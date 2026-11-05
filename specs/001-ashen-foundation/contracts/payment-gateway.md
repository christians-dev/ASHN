# Payment Gateway Port

Internal to the Payments module. Phase 1 has one implementation, `SimulatedPaymentGateway`.
A real provider (e.g. Stripe) is added later as another implementation plus a DI registration
change (Principle III, FR-026).

```csharp
internal interface IPaymentGateway
{
    bool IsSimulated { get; }                                     // drives FR-028 notice
    Task<TokenizeResult> SaveCard(CardDetails card);              // → token, brand, last4, expiry
    Task<GatewayResult> Authorize(Money amount, string cardTokenOrOneTimeToken, string idempotencyKey);
    Task<GatewayResult> Capture(string gatewayReference);
    Task<GatewayResult> Void(string gatewayReference);
    Task<GatewayResult> Refund(string gatewayReference, Money amount);
}
internal record GatewayResult(bool Succeeded, string? GatewayReference, DeclineCode? Decline);
internal enum DeclineCode { CardDeclined, InsufficientFunds, ExpiredCard, InvalidCard }
```

## Simulated behaviour

| Card number | Result |
|---|---|
| `4242 4242 4242 4242` | Approve (Visa) |
| `5555 5555 5555 4444` | Approve (Mastercard) |
| `4000 0000 0000 0002` | Decline: `CardDeclined` |
| `4000 0000 0000 9995` | Decline: `InsufficientFunds` |
| Fails Luhn check | Rejected: `InvalidCard` (SaveCard and Authorize) |
| Expiry in the past | Rejected: `ExpiredCard` |
| Any other Luhn-valid number | Approve |

- Stateless: tokens encode brand, last 4 and expiry (`sim_card_<brand>_<last4>_<MMYY>_<random>`),
  and the outcome is decided from those alone (last 4 `0002` → declined, `9995` → insufficient
  funds, expired → expired, else approve). Saved cards therefore behave the same after a restart.
  References: `sim_pay_<guid>`. CVC: any 3–4 digits; never stored.
- `Authorize` with the same idempotency key returns the same result.
- Capture/Void/Refund always succeed for a reference the simulator issued.
- Configuration flag `Payments:SimulateLatencyMs` (default 0) to make loading states visible.
