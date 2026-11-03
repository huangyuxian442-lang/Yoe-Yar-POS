using FreshMart.Pos.Models;

namespace FreshMart.Pos.Services;

/// The sale currently being rung up. Lives for the app's lifetime so it survives page switches.
public class CartService(Loc L)
{
    record HeldSale(List<CartLine> Lines, decimal Discount, Customer? Customer, PackType Mode);

    public List<CartLine> Lines { get; private set; } = [];
    public decimal DiscountPct { get; set; }
    public Customer? Customer { get; private set; }

    /// Scans and taps add items in this pack type. Wholesale customers switch it to CTN.
    public PackType Mode { get; private set; } = PackType.Pcs;

    HeldSale? _held;
    public bool HasHeld => _held is not null;

    public event Action? Changed;
    void Notify() => Changed?.Invoke();

    public bool IsEmpty => Lines.Count == 0;

    public void SetMode(PackType mode) { Mode = mode; Notify(); }
    public void ToggleMode() => SetMode(Mode == PackType.Pcs ? PackType.Ctn : PackType.Pcs);

    public void SetCustomer(Customer? c)
    {
        Customer = c;
        Mode = c?.IsWholesale == true ? PackType.Ctn : PackType.Pcs;
        Notify();
    }

    /// Pieces of a product already in the cart, across PCS and CTN lines.
    public decimal BaseQtyInCart(int productId, CartLine? except = null) =>
        Lines.Where(l => l.ProductId == productId && l != except).Sum(l => l.BaseQty);

    string StockError(Product p) => L.F("only_in_stock", Fmt.Stock(p, L), L.Name(p));

    /// Adds in the current mode (falls back to PCS when the product has no carton price).
    /// Returns an error message, or null when the item was added.
    public string? Add(Product p, decimal qty = 1, PackType? pack = null)
    {
        var pk = pack ?? Mode;
        if (pk == PackType.Ctn && !p.HasCtn) pk = PackType.Pcs;
        var units = p.UnitsFor(pk);
        if (BaseQtyInCart(p.Id) + qty * units > p.Stock) return StockError(p);

        var line = p.Unit == SellUnit.Each ? Lines.FirstOrDefault(l => l.ProductId == p.Id && l.Pack == pk) : null;
        if (line is not null) line.Qty += qty;
        else Lines.Add(new CartLine
        {
            ProductId = p.Id, Name = p.Name, NameEn = p.NameEn, Unit = p.Unit,
            Pack = pk, UnitsPerPack = units, Price = p.PriceFor(pk), Cost = p.Cost * units, Qty = qty,
        });
        Notify();
        return null;
    }

    public string? SetQty(CartLine line, decimal qty, Product p)
    {
        if (qty <= 0) { Remove(line); return null; }
        if (BaseQtyInCart(line.ProductId, line) + qty * line.UnitsPerPack > p.Stock) return StockError(p);
        line.Qty = qty;
        Notify();
        return null;
    }

    /// Switch a line between PCS and CTN, keeping the quantity number.
    public string? TogglePack(CartLine line, Product p)
    {
        if (!p.HasCtn) return L["no_ctn_price"];
        var pk = line.Pack == PackType.Pcs ? PackType.Ctn : PackType.Pcs;
        var units = p.UnitsFor(pk);
        if (BaseQtyInCart(p.Id, line) + line.Qty * units > p.Stock) return StockError(p);
        var twin = Lines.FirstOrDefault(l => l != line && l.ProductId == p.Id && l.Pack == pk);
        if (twin is not null) { twin.Qty += line.Qty; Lines.Remove(line); }
        else { line.Pack = pk; line.UnitsPerPack = units; line.Price = p.PriceFor(pk); line.Cost = p.Cost * units; }
        Notify();
        return null;
    }

    public void Remove(CartLine line) { Lines.Remove(line); Notify(); }

    public void Clear()
    {
        Lines = []; DiscountPct = 0; Customer = null; Mode = PackType.Pcs;
        Notify();
    }

    public string? Hold()
    {
        if (IsEmpty) return L["nothing_to_hold"];
        if (_held is not null) return L["already_held"];
        _held = new HeldSale(Lines, DiscountPct, Customer, Mode);
        Lines = []; DiscountPct = 0; Customer = null; Mode = PackType.Pcs;
        Notify();
        return null;
    }

    public string? Resume()
    {
        if (_held is null) return null;
        if (!IsEmpty) return L["finish_current_first"];
        (Lines, DiscountPct, Customer, Mode) = (_held.Lines, _held.Discount, _held.Customer, _held.Mode);
        _held = null;
        Notify();
        return null;
    }
}
