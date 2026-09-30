using FFXIVClientStructs.FFXIV.Client.Game;

namespace GlamourZones.Game;

// Whose house you're inside, if any.
public static class Housing
{
    public static unsafe HouseKind Current()
    {
        var housing = HousingManager.Instance();
        if (housing == null || !housing->IsInside())
            return HouseKind.None;
        var here = housing->GetCurrentHouseId();
        if (!Valid(here))
            return HouseKind.None;

        var kinds = HouseKind.None;
        if (SameEstate(here, HousingManager.GetOwnedHouseId(EstateType.PersonalEstate, 0))
            || SameEstate(here, HousingManager.GetOwnedHouseId(EstateType.SharedEstate, 0))
            || SameEstate(here, HousingManager.GetOwnedHouseId(EstateType.SharedEstate, 1)))
            kinds |= HouseKind.MyHouse;
        if (SameEstate(here, HousingManager.GetOwnedHouseId(EstateType.FreeCompanyEstate, 0)))
            kinds |= HouseKind.MyFreeCompanyHouse;
        if (SameRoom(here, HousingManager.GetOwnedHouseId(EstateType.PersonalChambers, 0)))
            kinds |= HouseKind.MyPrivateChambers;
        if (SameRoom(here, HousingManager.GetOwnedHouseId(EstateType.ApartmentRoom, 0)))
            kinds |= HouseKind.MyApartment;
        return kinds == HouseKind.None ? HouseKind.SomeoneElses : kinds;
    }

    private static bool Valid(HouseId id) => id.Id != 0 && id.Id != ulong.MaxValue;

    private static bool SameEstate(HouseId here, HouseId owned) =>
        Valid(owned) && (here.Id == owned.Id || !owned.IsApartment && !here.IsApartment
        && here.WorldId == owned.WorldId && here.WardIndex == owned.WardIndex && here.PlotIndex == owned.PlotIndex
        && here.TerritoryTypeId == owned.TerritoryTypeId);

    private static bool SameRoom(HouseId here, HouseId owned) =>
        Valid(owned) && (here.Id == owned.Id || here.WorldId == owned.WorldId && here.WardIndex == owned.WardIndex
        && here.IsApartment == owned.IsApartment && here.RoomNumber == owned.RoomNumber
        && (owned.IsApartment ? here.ApartmentDivision == owned.ApartmentDivision : here.PlotIndex == owned.PlotIndex)
        && here.TerritoryTypeId == owned.TerritoryTypeId);
}
