using System.Collections.ObjectModel;
using System.Windows.Input;
using Fidelizare.App.Infrastructure;
using Fidelizare.Core.Abstractions;
using Fidelizare.Core.Model;
using Fidelizare.Core.Services;

namespace Fidelizare.App.ViewModels;

/// <summary>O recompensă împreună cu motivul pentru care nu poate fi acordată acum.</summary>
public sealed class RewardOption
{
    public required Reward Reward { get; init; }

    /// <summary>Null dacă recompensa poate fi acordată.</summary>
    public string? Blocker { get; init; }

    public bool CanRedeem => Blocker is null;

    public string Name => Reward.Name;
    public string PointsLabel => $"{Reward.PointsCost} puncte";
    public string ValueLabel => Reward.ValueLabel;
    public string? Description => Reward.Description;
}

/// <summary>Felul mesajului afișat după o operațiune, care îi dă și culoarea.</summary>
public enum StatusKind
{
    None,
    Success,
    Warning,
    Error,
}

/// <summary>
/// Ecranul de casă. Ordinea în care sunt gândite lucrurile urmează ordinea de la
/// tejghea: întâi se identifică clientul, apoi se trece bonul, apoi eventual se
/// acordă o recompensă.
/// </summary>
public sealed class PosViewModel : ViewModelBase
{
    private readonly LoyaltyService _loyalty;
    private readonly AppServices _services;

    private string _searchText = string.Empty;
    private CustomerSummary? _summary;
    private string _grossAmount = string.Empty;
    private string _discountAmount = string.Empty;
    private string _receiptNumber = string.Empty;
    private string _statusMessage = string.Empty;
    private StatusKind _statusKind = StatusKind.None;
    private bool _searchPerformed;

    /// <summary>
    /// Campania care s-ar aplica bonului curent. Se recalculează la schimbarea
    /// sumei, nu la fiecare citire: proprietățile sunt citite de interfață de
    /// zeci de ori, iar fiecare verificare înseamnă o interogare în baza de date.
    /// </summary>
    private Campaign? _campaign;

    /// <summary>
    /// Recompensa acordată ultima dată pentru bonul în lucru, cât timp mai poate
    /// fi anulată. Se uită în momentul în care vânzarea este înregistrată: după
    /// aceea, anularea ar lăsa pe bon o reducere fără acoperire în puncte.
    /// </summary>
    private Redemption? _lastRedemption;

    /// <summary>Cât din reducerea curentă provine din puncte, nu din decizia comercială a casierului.</summary>
    private decimal _pointsDiscount;

    /// <summary>
    /// Adevarat cat timp singurul cont din program este cel de administrator.
    /// Atunci jurnalul de audit nu poate distinge intre persoane, iar avertismentul
    /// ramane pe ecran pana cand se creeaza primul cont de lucru.
    /// </summary>
    private bool _onlyAdminAccount;

    public PosViewModel()
    {
        _services = AppServices.Current;
        _loyalty = _services.Loyalty;

        Rules = _loyalty.GetRules();
        RefreshAccountHint();

        SearchCommand = new RelayCommand(Search);
        ClearCommand = new RelayCommand(Clear);
        SelectCustomerCommand = new RelayCommand(p => SelectCustomer(p as Customer));
        RecordSaleCommand = new RelayCommand(RecordSale, () => Summary is not null);
        RedeemCommand = new RelayCommand(p => Redeem(p as RewardOption));
        BirthdayBonusCommand = new RelayCommand(GrantBirthdayBonus, () => BirthdayBonusAvailable);
        UndoRedemptionCommand = new RelayCommand(UndoRedemption, () => CanUndoRedemption);
    }

    public LoyaltyRules Rules { get; private set; }

    public ObservableCollection<Customer> SearchResults { get; } = [];

    public ObservableCollection<RewardOption> Rewards { get; } = [];

    public ObservableCollection<PointTransaction> History { get; } = [];

    public ICommand SearchCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand SelectCustomerCommand { get; }
    public ICommand RecordSaleCommand { get; }
    public ICommand RedeemCommand { get; }
    public ICommand BirthdayBonusCommand { get; }
    public ICommand UndoRedemptionCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public CustomerSummary? Summary
    {
        get => _summary;
        private set
        {
            if (!SetProperty(ref _summary, value))
                return;

            OnPropertyChanged(
                nameof(HasCustomer), nameof(CustomerName), nameof(CardLabel), nameof(TierName),
                nameof(TierColor), nameof(PointsBalance), nameof(PointsValueLabel), nameof(ProgressLabel),
                nameof(ProgressPercent), nameof(ExpiryWarning), nameof(HasExpiryWarning),
                nameof(BirthdayBonusAvailable), nameof(ContactLabel), nameof(LastPurchaseLabel),
                nameof(LifetimeLabel), nameof(TierBenefits));
        }
    }

    public bool HasCustomer => Summary is not null;

    /// <summary>Adevărat când s-a căutat ceva și nu s-a găsit nimic.</summary>
    public bool ShowNoResults => _searchPerformed && SearchResults.Count == 0 && Summary is null;

    public bool ShowResultList => SearchResults.Count > 1;

    // ------------------------------------------------------- Fișa clientului

    public string CustomerName => Summary?.Customer.FullName ?? string.Empty;

    public string CardLabel => Summary is null ? string.Empty : $"Card {Summary.Customer.CardCode}";

    public string ContactLabel
    {
        get
        {
            if (Summary is null)
                return string.Empty;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Summary.Customer.Phone))
                parts.Add(FormatPhone(Summary.Customer.Phone));
            if (!string.IsNullOrWhiteSpace(Summary.Customer.Email))
                parts.Add(Summary.Customer.Email);

            return string.Join("  ·  ", parts);
        }
    }

    public string TierName => Summary?.Tier.Name ?? string.Empty;

    public string TierColor => Summary?.Tier.ColorHex ?? "#6B7280";

    public string? TierBenefits => Summary?.Tier.Benefits;

    public int PointsBalance => Summary?.PointsBalance ?? 0;

    public string PointsValueLabel
    {
        get
        {
            if (Summary is null)
                return string.Empty;

            var value = PointsCalculator.EstimateValue(Summary.PointsBalance, Rules);
            return $"echivalent aproximativ {value:N2} lei";
        }
    }

    /// <summary>Textul de sub bara de progres către nivelul următor.</summary>
    public string ProgressLabel
    {
        get
        {
            if (Summary is null)
                return string.Empty;

            if (Summary.NextTier is null)
                return $"Nivel maxim atins. Rulaj în perioada de calificare: {Summary.QualifyingValue:N2} lei.";

            return $"Încă {Summary.ValueToNextTier:N2} lei până la nivelul {Summary.NextTier.Name} " +
                   $"(rulaj actual {Summary.QualifyingValue:N2} lei).";
        }
    }

    /// <summary>Cât s-a parcurs din intervalul dintre nivelul curent și următorul, în procente.</summary>
    public double ProgressPercent
    {
        get
        {
            if (Summary?.NextTier is null)
                return 100d;

            var start = Summary.Tier.Threshold;
            var end = Summary.NextTier.Threshold;
            var span = end - start;

            if (span <= 0)
                return 100d;

            var done = (double)((Summary.QualifyingValue - start) / span) * 100d;
            return Math.Clamp(done, 0d, 100d);
        }
    }

    public bool ShowSingleAccountHint => _onlyAdminAccount;

    public bool HasPointsDiscount => _pointsDiscount > 0m;

    /// <summary>Reamintirea că reducerea trebuie trecută și pe casa de marcat.</summary>
    public string PointsDiscountLabel =>
        $"Reducere din puncte: {_pointsDiscount:N2} lei. Trece-o și pe casa de marcat înainte de încasare.";

    /// <summary>
    /// Adevărat cât timp casierul își mai poate anula singur recompensa: este a lui,
    /// nu a expirat intervalul, iar vânzarea nu a fost încă înregistrată.
    /// </summary>
    public bool CanUndoRedemption
    {
        get
        {
            if (_lastRedemption is null)
                return false;

            if (_services.RequireUser.CanManagePoints)
                return true;

            var minutes = (_services.Clock.Now - _lastRedemption.OccurredAt).TotalMinutes;
            return Rules.SelfVoidMinutes > 0 && minutes <= Rules.SelfVoidMinutes;
        }
    }

    public string UndoRedemptionLabel =>
        _lastRedemption is null ? string.Empty : $"Anulează „{_lastRedemption.RewardName}”";

    public bool HasCampaign => _campaign is not null;

    /// <summary>Textul benzii care anunță campania activă, afișat la casă.</summary>
    public string CampaignLabel => _campaign is null
        ? string.Empty
        : $"Campanie activă: {_campaign.Name} — puncte ×{_campaign.Multiplier:0.##}" +
          (_campaign.MinPurchaseAmount > 0 ? $", de la {_campaign.MinPurchaseAmount:N2} lei" : string.Empty);

    public bool HasExpiryWarning => Summary is { PointsExpiringSoon: > 0 };

    public string ExpiryWarning
    {
        get
        {
            if (Summary is null || Summary.PointsExpiringSoon <= 0)
                return string.Empty;

            var date = Summary.NextExpiryAt;
            return date is null
                ? $"{Summary.PointsExpiringSoon} puncte expiră în curând."
                : $"{Summary.PointsExpiringSoon} puncte expiră pe {date:dd.MM.yyyy}. Merită folosite acum.";
        }
    }

    public bool BirthdayBonusAvailable =>
        Summary is not null &&
        PointsCalculator.IsBirthdayBonusDue(Summary.Customer, Rules, _services.Clock.Now);

    public string LastPurchaseLabel => Summary?.LastPurchaseAt is { } date
        ? $"Ultima cumpărare: {date:dd.MM.yyyy}"
        : "Nicio cumpărare înregistrată";

    public string LifetimeLabel => Summary is null
        ? string.Empty
        : $"{Summary.PurchaseCount} cumpărări · {Summary.LifetimeSpend:N2} lei de la înscriere";

    // ---------------------------------------------------------- Bonul curent

    public string GrossAmount
    {
        get => _grossAmount;
        set
        {
            if (SetProperty(ref _grossAmount, value))
            {
                RefreshCampaign();
                OnPropertyChanged(nameof(NetPreview), nameof(PointsPreview),
                    nameof(CampaignLabel), nameof(HasCampaign));
                // Unele recompense au prag minim de bon, deci disponibilitatea lor
                // se schimbă pe măsură ce se tastează suma.
                RefreshRewards();
            }
        }
    }

    public string DiscountAmount
    {
        get => _discountAmount;
        set
        {
            if (SetProperty(ref _discountAmount, value))
            {
                RefreshCampaign();
                OnPropertyChanged(nameof(NetPreview), nameof(PointsPreview),
                    nameof(CampaignLabel), nameof(HasCampaign));
            }
        }
    }

    public string ReceiptNumber
    {
        get => _receiptNumber;
        set => SetProperty(ref _receiptNumber, value);
    }

    private decimal Gross => AmountParser.ParseOrZero(GrossAmount);
    private decimal Discount => AmountParser.ParseOrZero(DiscountAmount);
    private decimal Net => Math.Max(0m, Gross - Discount);

    public string NetPreview => Gross <= 0 ? "—" : $"{Net:N2} lei";

    /// <summary>Câte puncte va primi clientul, calculat înainte de a apăsa butonul.</summary>
    public string PointsPreview
    {
        get
        {
            if (Summary is null || Gross <= 0)
                return "—";

            var campaignMultiplier = _campaign?.Multiplier ?? 1m;
            var points = PointsCalculator.PointsForPurchase(
                Net, Summary.Tier.EarnMultiplier * campaignMultiplier, Rules);

            var reasons = new List<string>();

            if (Summary.Tier.EarnMultiplier > 1m)
                reasons.Add($"nivel {Summary.Tier.Name} ×{Summary.Tier.EarnMultiplier:0.##}");

            if (_campaign is not null)
                reasons.Add($"campanie ×{_campaign.Multiplier:0.##}");

            return reasons.Count == 0
                ? $"{points} puncte"
                : $"{points} puncte (include {string.Join(" și ", reasons)})";
        }
    }

    // ------------------------------------------------------------- Mesaje

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public StatusKind StatusKind
    {
        get => _statusKind;
        private set => SetProperty(ref _statusKind, value);
    }

    // ------------------------------------------------------------ Acțiuni

    public void Search()
    {
        _searchPerformed = true;
        SearchResults.Clear();
        ClearStatus();

        var text = SearchText.Trim();
        if (text.Length == 0)
        {
            Summary = null;
            NotifySearchState();
            return;
        }

        // Întâi identificarea exactă: card scanat sau telefon tastat complet.
        var exact = _loyalty.FindByCardOrPhone(text);
        if (exact is not null)
        {
            SelectCustomer(exact);
            SearchText = string.Empty;
            NotifySearchState();
            return;
        }

        var matches = _loyalty.SearchCustomers(new CustomerQuery { SearchText = text, Take = 25 });

        foreach (var match in matches)
            SearchResults.Add(match);

        if (matches.Count == 1)
        {
            SelectCustomer(matches[0]);
            SearchText = string.Empty;
            SearchResults.Clear();
        }
        else if (matches.Count == 0)
        {
            Summary = null;
        }

        NotifySearchState();
    }

    public void SelectCustomer(Customer? customer)
    {
        if (customer is null)
            return;

        Summary = _loyalty.GetSummary(customer.Id);
        SearchResults.Clear();
        SearchText = string.Empty;

        _lastRedemption = null;
        _pointsDiscount = 0m;

        RefreshCampaign();
        RefreshRewards();
        RefreshHistory();
        NotifySearchState();
        NotifyRedemptionState();
        OnPropertyChanged(nameof(CampaignLabel), nameof(HasCampaign));
    }

    /// <summary>Reîncarcă fișa clientului curent, după o operațiune care i-a schimbat soldul.</summary>
    public void ReloadCustomer()
    {
        if (Summary is null)
            return;

        Summary = _loyalty.GetSummary(Summary.Customer.Id);
        RefreshRewards();
        RefreshHistory();
    }

    public void Clear()
    {
        Summary = null;
        SearchText = string.Empty;
        GrossAmount = string.Empty;
        DiscountAmount = string.Empty;
        ReceiptNumber = string.Empty;
        SearchResults.Clear();
        Rewards.Clear();
        History.Clear();
        _lastRedemption = null;
        _pointsDiscount = 0m;
        _searchPerformed = false;
        NotifyRedemptionState();
        ClearStatus();
        NotifySearchState();
    }

    private void RecordSale()
    {
        if (Summary is null)
            return;

        if (Gross <= 0)
        {
            SetStatus("Completează valoarea bonului.", StatusKind.Error);
            return;
        }

        var result = _loyalty.RecordPurchase(new PurchaseRequest
        {
            CustomerId = Summary.Customer.Id,
            GrossAmount = Gross,
            DiscountAmount = Discount,
            ReceiptNumber = ReceiptNumber,
        }, _services.RequireUser);

        if (result.Failed)
        {
            SetStatus(result.Error!, StatusKind.Error);
            return;
        }

        var outcome = result.Value!;
        Summary = outcome.Summary;

        var message = outcome.PointsEarned > 0
            ? $"Vânzare înregistrată. Clientul a primit {outcome.PointsEarned} puncte și are acum {outcome.Summary.PointsBalance}."
            : $"Vânzare înregistrată. Bonul nu a generat puncte (prag minim {Rules.MinPurchaseForPoints:N2} lei).";

        if (outcome.PromotedToTier is not null)
            message += $" A avansat la nivelul {outcome.PromotedToTier.Name}.";

        SetStatus(message, StatusKind.Success);

        // Vânzarea este închisă: recompensa nu mai poate fi anulată de aici, fiindcă
        // bonul înregistrat poartă deja reducerea. De acum, corectura trece prin
        // anularea vânzării, care este operațiune de responsabil.
        _lastRedemption = null;
        _pointsDiscount = 0m;

        GrossAmount = string.Empty;
        DiscountAmount = string.Empty;
        ReceiptNumber = string.Empty;

        RefreshRewards();
        RefreshHistory();
        NotifyRedemptionState();
    }

    private void Redeem(RewardOption? option)
    {
        if (option is null || Summary is null)
            return;

        if (!option.CanRedeem)
        {
            SetStatus(option.Blocker!, StatusKind.Warning);
            return;
        }

        var basket = Gross > 0 ? Net : 0m;

        var confirmation =
            $"Acorzi „{option.Reward.Name}” clientului {Summary.Customer.FullName}?\n\n" +
            $"Se scad {option.Reward.PointsCost} puncte din cele {Summary.PointsBalance} disponibile.";

        if (!Dialogs.Confirm(confirmation))
            return;

        var result = _loyalty.RedeemReward(new RedeemRequest
        {
            CustomerId = Summary.Customer.Id,
            RewardId = option.Reward.Id,
            BasketAmount = basket,
            ReceiptNumber = ReceiptNumber,
        }, _services.RequireUser);

        if (result.Failed)
        {
            SetStatus(result.Error!, StatusKind.Error);
            ReloadCustomer();
            return;
        }

        var outcome = result.Value!;
        Summary = outcome.Summary;
        _lastRedemption = outcome.Redemption;

        // Reducerea se scrie singură în câmpul bonului. Retastarea ei era locul cel
        // mai probabil de greșeală: o cifră lipsă însemna client încasat integral
        // deși tocmai își plătise cu punctele.
        if (outcome.Redemption.Kind != RewardKind.FreeProduct && outcome.DiscountValue > 0m)
            ApplyPointsDiscount(outcome.DiscountValue);

        var message = outcome.Redemption.Kind == RewardKind.FreeProduct
            ? $"Recompensa „{outcome.Redemption.RewardName}” a fost acordată. Predă produsul clientului."
            : $"Reducere de {outcome.DiscountValue:N2} lei, trecută în bon. Aplic-o și pe casa de marcat.";

        SetStatus($"{message} Sold rămas: {outcome.Summary.PointsBalance} puncte.", StatusKind.Success);

        RefreshRewards();
        RefreshHistory();
        NotifyRedemptionState();
    }

    private void GrantBirthdayBonus()
    {
        if (Summary is null)
            return;

        var result = _loyalty.GrantBirthdayBonus(Summary.Customer.Id, _services.RequireUser);

        if (result.Failed)
        {
            SetStatus(result.Error!, StatusKind.Warning);
            return;
        }

        SetStatus($"Bonus de zi de naștere acordat: {result.Value} puncte.", StatusKind.Success);
        ReloadCustomer();
    }

    /// <summary>Adaugă reducerea obținută din puncte peste cea deja trecută pe bon.</summary>
    private void ApplyPointsDiscount(decimal amount)
    {
        _pointsDiscount += amount;
        DiscountAmount = AmountParser.Format(Discount + amount);
    }

    /// <summary>
    /// Anulează recompensa tocmai acordată și scoate reducerea din bon.
    ///
    /// Motivul este scris automat: la casă, cu clientul în față, o casetă în care
    /// trebuie tastată o explicație ar face corectura mai costisitoare decât
    /// greșeala. Cine și când a anulat rămâne oricum în jurnalul de audit.
    /// </summary>
    private void UndoRedemption()
    {
        if (_lastRedemption is null || Summary is null)
            return;

        var confirmation =
            $"Anulezi recompensa „{_lastRedemption.RewardName}”?\n\n" +
            $"Cele {_lastRedemption.PointsSpent} puncte se întorc la client, " +
            "iar reducerea se scoate din bon.";

        if (!Dialogs.Confirm(confirmation))
            return;

        var result = _loyalty.VoidRedemption(
            _lastRedemption.Id, "Anulată de casier imediat după acordare", _services.RequireUser);

        if (result.Failed)
        {
            SetStatus(result.Error!, StatusKind.Warning);
            ReloadCustomer();
            NotifyRedemptionState();
            return;
        }

        // Reducerea se scade doar dacă mai este acolo: casierul poate fi modificat
        // câmpul între timp, iar în acest caz cifra lui rămâne neatinsă.
        var toRemove = Math.Min(_lastRedemption.DiscountValue, Discount);
        if (toRemove > 0m)
        {
            _pointsDiscount = Math.Max(0m, _pointsDiscount - toRemove);
            DiscountAmount = Discount - toRemove <= 0m
                ? string.Empty
                : AmountParser.Format(Discount - toRemove);
        }

        SetStatus(
            $"Recompensa a fost anulată. Punctele au fost restituite, iar reducerea scoasă din bon.",
            StatusKind.Success);

        _lastRedemption = null;
        ReloadCustomer();
        NotifyRedemptionState();
    }

    private void NotifyRedemptionState() => OnPropertyChanged(
        nameof(CanUndoRedemption), nameof(UndoRedemptionLabel),
        nameof(HasPointsDiscount), nameof(PointsDiscountLabel));

    /// <summary>Reia campania potrivită pentru valoarea curentă a bonului.</summary>
    private void RefreshCampaign()
    {
        _campaign = Gross > 0 ? _loyalty.GetActiveCampaign(Net) : _loyalty.GetActiveCampaign(0m);
    }

    private void RefreshRewards()
    {
        Rewards.Clear();

        if (Summary is null)
            return;

        var basket = Gross > 0 ? Net : 0m;

        foreach (var (reward, blocker) in _loyalty.GetRewardsFor(Summary.Customer.Id, basket))
            Rewards.Add(new RewardOption { Reward = reward, Blocker = blocker });
    }

    private void RefreshHistory()
    {
        History.Clear();

        if (Summary is null)
            return;

        using var uow = _services.Factory.Create();
        foreach (var entry in uow.Points.GetTransactions(Summary.Customer.Id, 40))
            History.Add(entry);
    }

    /// <summary>Verifică dacă programul are încă un singur cont, cel de administrator.</summary>
    private void RefreshAccountHint()
    {
        try
        {
            _onlyAdminAccount = _services.RequireUser.Role == UserRole.Admin &&
                                _services.Auth.GetUsers().Count <= 1;
        }
        catch (Exception)
        {
            // Un avertisment care nu se poate calcula nu trebuie să oprească ecranul de casă.
            _onlyAdminAccount = false;
        }
    }

    /// <summary>Reîncarcă regulile, după ce administratorul le-a schimbat.</summary>
    public void ReloadRules()
    {
        Rules = _loyalty.GetRules();
        RefreshAccountHint();
        OnPropertyChanged(nameof(ShowSingleAccountHint));
        OnPropertyChanged(nameof(Rules), nameof(PointsPreview), nameof(PointsValueLabel));
        RefreshRewards();
    }

    private void SetStatus(string message, StatusKind kind)
    {
        StatusMessage = message;
        StatusKind = kind;
    }

    private void ClearStatus() => SetStatus(string.Empty, StatusKind.None);

    private void NotifySearchState() =>
        OnPropertyChanged(nameof(ShowNoResults), nameof(ShowResultList));

    /// <summary>Telefonul, grupat ca pe hârtie: 0722 123 456.</summary>
    private static string FormatPhone(string phone) =>
        phone.Length == 10
            ? $"{phone[..4]} {phone[4..7]} {phone[7..]}"
            : phone;
}
