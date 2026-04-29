namespace StandaloneTrajectoryCalculator;

public static class PorkchopCalculator
{
    public static PorkchopWindow ResolveWindow(CalculationRequest request, OrbitalBody origin, OrbitalBody destination)
    {
        var hohmannTimeOfFlight = TransferCalculator.HohmannTimeOfFlight(origin, destination);
        var synodicPeriod = TransferCalculator.SynodicPeriod(origin, destination);

        // Default departure start: prefer provided value, otherwise use current UTC
        // (converted to J2000 seconds). Using epoch 0 by default caused unexpected
        // windows for GUI users who didn't set a start time.
        var departureStart = request.DepartureWindowStart ?? SolarSystemCatalog.ToJ2000Seconds(DateTimeOffset.UtcNow);

        // Покрываем ровно один синодический период (один цикл противостояния),
        // чтобы скан захватывал одно основное окно перелёта, если пользователь
        // не указал явный конец окна.
        var departureRange = synodicPeriod;
        var departureEnd = request.DepartureWindowEnd ?? (departureStart + departureRange);

        // Travel time defaults: center around Hohmann TOF but provide a broad
        // search window to accommodate non-Hohmann transfers.
        var travelTimeMin = request.TravelTimeMin ?? Math.Max(hohmannTimeOfFlight * 0.5, hohmannTimeOfFlight - destination.Orbit.Period);
        var travelTimeMax = request.TravelTimeMax ?? Math.Max(travelTimeMin + destination.Orbit.Period, hohmannTimeOfFlight * 2);

        if (departureEnd <= departureStart)
        {
            throw new InvalidOperationException("Departure window end must be greater than departure window start.");
        }

        if (travelTimeMax <= travelTimeMin)
        {
            throw new InvalidOperationException("Travel-time maximum must be greater than travel-time minimum.");
        }

        if (request.DepartureSteps <= 0 || request.TravelTimeSteps <= 0)
        {
            throw new InvalidOperationException("Porkchop grid step counts must be positive.");
        }

        return new PorkchopWindow
        {
            DepartureStart = departureStart,
            DepartureEnd = departureEnd,
            TravelTimeMin = travelTimeMin,
            TravelTimeMax = travelTimeMax,
            DepartureSteps = request.DepartureSteps,
            TravelTimeSteps = request.TravelTimeSteps
        };
    }

    public static PorkchopResult Calculate(
        OrbitalBody origin,
        OrbitalBody destination,
        CentralBody centralBody,
        CalculationRequest request)
    {
        var window = ResolveWindow(request, origin, destination);
        var points = new List<PorkchopPoint>(window.DepartureSteps * window.TravelTimeSteps);

        var departureResolution = window.DepartureSteps == 1
            ? 0
            : (window.DepartureEnd - window.DepartureStart) / (window.DepartureSteps - 1);

        var travelResolution = window.TravelTimeSteps == 1
            ? 0
            : (window.TravelTimeMax - window.TravelTimeMin) / (window.TravelTimeSteps - 1);

        TransferDetails? bestTransfer = null;
        var validPoints = 0;
        var invalidPoints = 0;

        for (var x = 0; x < window.DepartureSteps; x++)
        {
            var departureTime = window.DepartureStart + x * departureResolution;

            for (var y = 0; y < window.TravelTimeSteps; y++)
            {
                var travelTime = window.TravelTimeMin + y * travelResolution;
                TransferDetails? transfer;

                try
                {
                    transfer = TransferCalculator.CalculateTransfer(
                        origin,
                        destination,
                        centralBody,
                        departureTime,
                        travelTime,
                        request.ResolveDepartureOrbitPeriapsisAltitude(),
                        request.ResolveDepartureOrbitApoapsisAltitude(),
                        request.ArrivalParkingOrbitAltitude,
                        request.ArrivalManeuverMode,
                        request.UseAerobraking,
                        request.LongWay);
                }
                catch
                {
                    transfer = null;
                }

                if (transfer is null)
                {
                    invalidPoints++;
                    points.Add(new PorkchopPoint
                    {
                        DepartureTime = departureTime,
                        TravelTime = travelTime,
                        TotalDeltaV = null,
                        LongWay = null
                    });
                    continue;
                }

                validPoints++;
                points.Add(new PorkchopPoint
                {
                    DepartureTime = departureTime,
                    TravelTime = travelTime,
                    TotalDeltaV = transfer.DVTotal,
                    LongWay = transfer.LongWay
                });

                if (bestTransfer is null || transfer.DVTotal < bestTransfer.DVTotal)
                {
                    bestTransfer = transfer;
                }
            }
        }

        if (bestTransfer is null)
        {
            throw new InvalidOperationException("No valid transfers were found in the porkchop window.");
        }

        return new PorkchopResult
        {
            Window = window,
            BestTransfer = bestTransfer,
            Points = points,
            ValidPoints = validPoints,
            InvalidPoints = invalidPoints,
            HohmannTimeOfFlight = TransferCalculator.HohmannTimeOfFlight(origin, destination),
            SynodicPeriod = TransferCalculator.SynodicPeriod(origin, destination)
        };
    }
}
