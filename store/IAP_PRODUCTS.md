# In-app products

The game asks Google Play for these identifiers at launch. An identifier that does not exist in the console is
silently missing from the shop: no price, and the buy button fails. They have to match exactly.

Prices are the ones the balance uses, in US dollars. Google converts them for every other country, so the figure
below is the only one to type.

## Consumable (bought again and again)

| Product ID | Price | Name | What the player gets |
|---|---|---|---|
| `crushroyale.orbes.pack1` | 0.99 | Handful of Orbs | 74 orbs |
| `crushroyale.orbes.pack2` | 4.99 | Pouch of Orbs | 395 orbs (385 + 10 bonus) |
| `crushroyale.orbes.pack3` | 12.99 | Chest of Orbs | 1,035 orbs (965 + 70 bonus) |
| `crushroyale.orbes.pack4` | 24.99 | Vault of Orbs | 2,200 orbs (2,100 + 100 bonus) |
| `crushroyale.orbes.pack5` | 49.99 | Hoard of Orbs | 5,500 orbs (5,250 + 250 bonus) |
| `crushroyale.orbes.pack6` | 99.99 | Dragon's Hoard | 12,000 orbs (11,000 + 1,000 bonus) |
| `crushroyale.orbes.mega` | 199.99 | Crystalheim Treasury | 30,000 orbs (27,500 + 2,500 bonus) |
| `crushroyale.battlepass` | 9.99 | Season Pass | the premium track of the current season |
| `crushroyale.piggybank` | 4.99 | Break the Piggy Bank | the orbs the jar has collected, up to 600 |

The battle pass is consumable because a new season starts every few weeks, and the piggy bank because it fills
again after it is broken. Google refuses to sell a durable product twice, so either of them registered as
durable would work once per account and then fail forever.

## Non-consumable (bought once, kept)

| Product ID | Price | Name | What the player gets |
|---|---|---|---|
| `crushroyale.removeads` | 4.99 | Remove Ads | no more interstitials, rewarded ads still available |
| `crushroyale.crown` | 19.99 | Crown of Crystalheim | the rare perk, permanently |

## What still has to happen before any of this takes money

1. **Create the eleven products** in Monetise with Play > Products > In-app products, and activate each one.
2. **Link a Google Play Developer API service account.** The server verifies every receipt against
   `purchases.products` before it grants anything ([ReceiptValidation.cs](../src/CrushRoyale.Server/Services/ReceiptValidation.cs)),
   and refuses to start with billing enabled and no credentials. The JSON key goes on the server, never in this
   repository, and `Iap:ServiceAccountJsonPath` points at it.
3. **Add the twelve testers as licence testers** (Setup > Licence testing, at account level). Without that they
   are charged real money; with it they see the real purchase flow and pay nothing.

Prices in the console and prices in the balance have to be changed together. The client shows Google's localised
price string, so a mismatch is invisible to the player, but the server grants what the balance says, which is
how a 0.99 purchase ends up handing out a 99.99 pack.
