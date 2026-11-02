# SMT Price Gun 200% — CZ

**Verze 0.7.2**

Client-side BepInEx 5 mod pro **Supermarket Together**.

## Použití

1. Vezmi pricing scanner / price gun.
2. Namiř na produkt.
3. Stiskni **prostřední tlačítko myši**.
4. Mod nastaví cenu na **200 % aktuální raw market price**.

Výchozí cena se zaokrouhluje dolů na centy.

## Konfigurace

Po prvním spuštění vznikne:

```text
BepInEx\config\cz.milan.supermarkettogether.pricegun200.cfg
```

Důležité hodnoty:

```ini
[Pricing]
MarketPriceMultiplier = 2
RoundDownStep = 0.01

[Scanner]
Hotkey = Mouse2
MaxDistance = 6
RequirePricingTool = true
```

`MarketPriceMultiplier = 2` znamená přesně 200 % market price.

## Multiplayer

Mod je navržený jako **client-only**. Nezapisuje cenu jen lokálně, ale hledá a volá běžnou herní metodu pro aktualizaci ceny (preferuje `CmdUpdateProductPrice`). Pokud aktuální build hry dovoluje klientovi normálně měnit ceny, host mod nepotřebuje.

Pokud host používá permissions mod, který změny cen blokuje, tento mod jej neobchází.

## Sestavení DLL

Nejjednodušší je spustit:

```text
build.cmd
```

Je potřeba mít nainstalovaný **.NET 8 SDK nebo novější**. První build potřebuje internet pro stažení NuGet balíčků.

Výsledná DLL bude zde:

```text
bin\Release\netstandard2.1\SMT.PriceGun200.dll
```

Tu zkopíruj do:

```text
Supermarket Together\BepInEx\plugins\SMT.PriceGun200\
```

## Když to po update hry nefunguje

V configu zapni:

```ini
[Debug]
VerboseLogging = true
```

Pak zkus cenu znovu nastavit a pošli obsah relevantní části:

```text
BepInEx\LogOutput.log
```

Mod vypíše, jaký collider trefil, komponenty a product/item/price/target fields v okolí, odkud získal product ID a jakou pricing metodu se pokusil zavolat.

## Změny ve 0.2.0

- podpora product ID uloženého jako enum (`productType`, `itemType`, apod.)
- hledání product dat na zasaženém objektu, rodičích, blízkých colliderech a sourozencích ve shelf/label hierarchii
- podpora pricing metod, které berou enum místo `int`
- výrazně podrobnější verbose diagnostika


## Změny ve 0.3.0

- ID zaměřeného produktu se čte z konkrétního řádku `Data_Container.productInfoArray`, pro produkty i cenovky.
- Resolver už neprochází katalogy ani globální pricing komponenty, které mohly vracet produkt #0.
- Regresní kontroly: `powershell -File Tests/verify.ps1`.


## Změny ve 0.3.1

- `RequirePricingTool = true` ověřuje scanner přes vybavení místního hráče (`PlayerNetwork.equippedItem == 2`), místo názvů objektů kamery.
- Výchozí nastavení vyžaduje scanner. Existující konfigurace se zachovává; ponech `RequirePricingTool = true`.


## Pokladna ve 0.4.0

Otevři obsluhu pokladny. Po převzetí hotovosti nebo karty stiskni **pravé tlačítko myši**. Hotovost doplní přesné vrácení (opraví i již zadanou částku); karta zadá a potvrdí správný součet. Akce se vztahuje jen na pokladnu otevřenou místním hráčem, vyžaduje herní oprávnění pokladníka a blokuje opakované potvrzení při čekání na server. Mimo otevřenou platbu mod pravý klik neobsluhuje.

```ini
[Checkout]
Enabled = true
ConfirmHotkey = Mouse1
```

Prostřední tlačítko a kontrola scanneru zůstávají pro nastavení cen. Ověření: `Tests/verify.ps1` a Release build; potvrzení v herním klientu je ještě potřeba vyzkoušet.


## Oprava ve 0.4.1

- Pokladna nevyžaduje camera event: zaměř pokladnu nebo terminál s aktivní platbou do vzdálenosti 4 metrů.
- Při stejné klávese má potvrzení aktivní platby přednost před nastavením ceny.
- Debug vypíše stisk klávesy pokladny a důvod odmítnutí akce. Klávesy z existujícího configu zůstávají zachované. `Mouse1` je pravé tlačítko, `Mouse2` prostřední.


## Použití pokladny ve 0.5.0

Namiř na nabídnutou kartu nebo peníze, případně na konkrétní ovládací tlačítko vrácení nebo terminálu. Stiskni `Checkout.ConfirmHotkey` (výchozí `Mouse1`; existující nastavení zůstává zachované). Mod použije herní akci převzetí, počká na aktivaci správného panelu, doplní částku a potvrdí platbu. Pokud je panel už otevřený, přejde rovnou k doplnění a potvrzení.

Podporované prvky: `Payments/Payment_Money`, `Payments/Payment_Card`, `CashRegister`, `CashRegisterCanvas`, `CreditCardCanvas` a jejich děti. Nesouvisející části regálu nebo cedule nic nespustí. Při čekání na server se další klik stejné transakce ignoruje.

Sestavení a regresní kontroly prošly. Herní DLL a prefab collidery byly zkontrolovány; samotnou platbu je třeba ověřit ve hře.


## Reconnect ve 0.5.1

Při každém požadavku na cenu se obnoví živá síťová instance `ProductListing`. Zaniklé, neaktivní nebo ještě nepřipojené instance se odmítnou; změna instance vymaže cache pricing metod. Lokální hráč se už vyhledává znovu při kliknutí. Log nyní uvádí odeslání požadavku, nikoli potvrzení změny serverem.

Regresní test reconnectu: `Tests/reconnect.ps1`; používá produkční resolver a simulaci Unity destroyed-object sémantiky. Reconnect v reálném multiplayeru je ještě potřeba vyzkoušet.


## Držení tlačítka ve 0.6.0

Namiř na pokladnu nebo její zboží na pásu a podrž `Checkout.ConfirmHotkey` déle než 0,3 s. Mod postupně skenuje zboží pouze této pokladny. Na každý kus pošle běžnou herní akci jednou a počká na jeho odebrání serverem. Pak čeká na kartu nebo peníze položené NPC, převezme skutečně nabídnutý způsob platby, doplní a potvrdí správnou částku.

Puštění tlačítka zastaví další lokální kroky; již odeslané síťové příkazy nelze vzít zpět. Automatika se také zastaví při ztrátě fokusu, změně lokálního hráče, odchodu přes 4 m od pokladny nebo ztrátě oprávnění pokladníka. Nezasahuje do pokladny obsazené NPC zaměstnancem. Krátký klik pro platbu zůstává dostupný.

```ini
[Checkout]
HoldToScan = true
ScanInterval = 0.15
```

Existující `ConfirmHotkey` se zachovává. Scanner je samostatný: mění pouze cenovky regálů a běží dál podle `Scanner.Hotkey` a `RequirePricingTool`.

Ověřeno: Release build, cenové/platební kontroly, gate proti dvojímu skenování a simulovaný reconnect. Držení tlačítka, skenování a platby po serverové odezvě je ještě potřeba vyzkoušet ve hře.


## Prodlevy plateb ve 0.6.1

```ini
[Checkout]
AfterPickupDelay = 1
ConfirmDelay = 1
```

Po převzetí karty/hotovosti a otevření příslušného herního panelu čeká mod `AfterPickupDelay` sekund, vyplní částku a pak čeká dalších `ConfirmDelay` sekund před potvrzením platby/vrácení. Platí pro hotovost i kartu, držení i krátký klik. Pokud je panel už otevřený, první prodleva běží od kliknutí. Rozsah obou hodnot 0–60 sekund, 0 prodlevu vypne. Puštění při držení zruší další lokální kroky i během čekání. Odeslané síťové příkazy nelze vzít zpět.


## F12 menu a optimalizace ve 0.7.0

**F12** otevře/zavře nastavení, **Esc** zavře menu nebo zruší změnu klávesy. Menu funguje i při vypnutém modu. Automatika během otevření nepokračuje; kurzor se uvolní a po zavření se vrátí původní stav. Před dalším držením je třeba pustit tlačítko.

Menu obsahuje přepínač celého modu, scanneru, požadavku drženého scanneru, pokladny, držení a debug logu. Lze změnit klávesy scanneru/pokladny, násobek ceny, zaokrouhlení, dosah, rychlost skenování a obě platební prodlevy. Čísla podporují desetinnou čárku i tečku.

- **Použít a uložit**: použije změny a uloží config jedním zápisem.
- **Reload configu**: znovu načte nastavení ze souboru, nahradí rozepsané změny. DLL se tím znovu nenačítá.
- **Zavřít**: zahodí neuložené úpravy menu.

Prodlevy pokladny už neprocházejí všechny assembly a všechny MonoBehaviour každý snímek. Úspěšně nalezené typy se cachují; lokální hráč se bere z aktuálního `FirstPersonController.Instance`. Po reconnectu se znovu naváže hráč i oprávnění, zaniklé Unity objekty se nepoužijí. Globální proměnné se obnoví při jejich výměně. Během čekání na platbu se znovu nehledá zboží na pásu; kontrola platby běží maximálně 20× za sekundu.

Kontroly: Release build; `Tests/verify.ps1`, `Tests/reconnect.ps1`, `Tests/context.ps1`. Test kontextu provádí 1000 cyklů bez opětovného hledání typů, komponent a proměnných a ověřuje reconnect i změnu oprávnění. Vzhled F12 menu a skutečnou změnu plynulosti je ještě nutné ověřit ve hře.


## Scanner a menu ve 0.7.1

Scanner používá herní `PlayerNetwork.interactableMask` a rozpozná regálový řádek podle `InteractableContainer`, i když má jiné názvy rodičů. Při každém kliknutí čte živé `productInfoArray`; nový produkt, výměna produktu nebo výměna celého pole nevyžaduje cache refresh. Neúspěšný klik vypíše konkrétní zasažený collider; debug ukáže i řádek bez přiřazeného produktu.

F12 okno má vlastní plně neprůhledné pozadí a UTF-8 texty. Test `Tests/shelf-live.ps1` ověřuje produkční resolver na průběžném doplnění a výměně produktů bez reconnectu. Reálný scénář nově zakoupeného zboží a vzhled menu je ještě nutné ověřit ve hře.


## Oprava colliderů pokladny ve 0.7.2

Herní prefab `7_CheckoutB` používá záporné měřítko a některé BoxCollidery se zápornou velikostí. `Checkout.FixNegativeColliders = true` nahrazuje vadné BoxCollidery jednoduchými konvexními box meshi na stejném GameObjectu. Zachová střed, absolutní rozměry, trigger, materiál, contact offset a aktivaci collideru. Vzhled ani transformace pokladny se nemění; původní BoxCollider zůstává vypnutý pro existující herní reference.

Oprava je výchozí zapnutá a přepíná se také ve F12. Vypnutí vrátí původní collidery; oprava se automaticky aplikuje na nově nalezené pokladny a uklízí vlastní meshe po jejich odstranění. Kontrola běží jednou za dvě sekundy, ne každý snímek. První varování při samotném vytvoření herního objektu se ještě může objevit, než jej runtime oprava najde.

`Tests/colliders.ps1` ověřuje produkční geometrii a životní cyklus v simulovaném Unity prostředí. Fyziku a ruční klikání na konkrétní pokladně je ještě potřeba potvrdit ve hře.
