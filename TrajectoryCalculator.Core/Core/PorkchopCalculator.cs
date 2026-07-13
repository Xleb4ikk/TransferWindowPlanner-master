namespace TrajectoryCalculator;

/// <summary>
/// Performs a porkchop scan over departure and arrival windows.
/// </summary>
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
        TransferDetails? bestShortWay = null;
        TransferDetails? bestLongWay = null;
        var validPoints = 0;
        var invalidPoints = 0;

        for (var x = 0; x < window.DepartureSteps; x++)
        {
            var departureTime = window.DepartureStart + x * departureResolution;

            for (var y = 0; y < window.TravelTimeSteps; y++)
            {
                var travelTime = window.TravelTimeMin + y * travelResolution;
                
                TransferDetails? shortWayTransfer = null;
                TransferDetails? longWayTransfer = null;

                // In "auto" mode (request.LongWay == null) evaluate BOTH Lambert
                // branches at every grid point and keep the cheaper one. Relying
                // on whichever branch is geometrically "natural" at a point (the
                // single-branch auto-resolution inside CalculateTransfer) can
                // silently hide a cheaper solution that exists on the other branch.
                if (request.LongWay is null or false)
                {
                    try
                    {
                        shortWayTransfer = TransferCalculator.CalculateTransfer(
                            origin, destination, centralBody, departureTime, travelTime,
                            request.ResolveDepartureOrbitPeriapsisAltitude(),
                            request.ResolveDepartureOrbitApoapsisAltitude(),
                            request.ArrivalParkingOrbitAltitude,
                            request.ArrivalManeuverMode,
                            request.UseAerobraking,
                            false);
                    }
                    catch
                    {
                        shortWayTransfer = null;
                    }
                }

                if (request.LongWay is null or true)
                {
                    try
                    {
                        longWayTransfer = TransferCalculator.CalculateTransfer(
                            origin, destination, centralBody, departureTime, travelTime,
                            request.ResolveDepartureOrbitPeriapsisAltitude(),
                            request.ResolveDepartureOrbitApoapsisAltitude(),
                            request.ArrivalParkingOrbitAltitude,
                            request.ArrivalManeuverMode,
                            request.UseAerobraking,
                            true);
                    }
                    catch
                    {
                        longWayTransfer = null;
                    }
                }

                var transfer = (shortWayTransfer, longWayTransfer) switch
                {
                    (not null, not null) => shortWayTransfer.DVTotal <= longWayTransfer.DVTotal ? shortWayTransfer : longWayTransfer,
                    (not null, null) => shortWayTransfer,
                    (null, not null) => longWayTransfer,
                    _ => null
                };

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

                if (shortWayTransfer is not null && (bestShortWay is null || shortWayTransfer.DVTotal < bestShortWay.DVTotal))
                {
                    bestShortWay = shortWayTransfer;
                }

                if (longWayTransfer is not null && (bestLongWay is null || longWayTransfer.DVTotal < bestLongWay.DVTotal))
                {
                    bestLongWay = longWayTransfer;
                }
            }
        }

        if (bestTransfer is null)
        {
            throw new InvalidOperationException("No valid transfers were found in the porkchop window.");
        }

        // Grid scan gives a result only at grid nodes. Refine with a local simplex
        // search around the best grid point to find the true dV minimum. In auto
        // mode, refine each branch independently — Nelder-Mead can't cross the
        // long/short-way boundary since Cost() holds longWay fixed — then keep
        // whichever refined branch is cheaper.
        if (request.LongWay is null)
        {
            var refinedShort = bestShortWay is not null
                ? RefineBestTransfer(origin, destination, centralBody, request, window, bestShortWay)
                : null;
            var refinedLong = bestLongWay is not null
                ? RefineBestTransfer(origin, destination, centralBody, request, window, bestLongWay)
                : null;

            TransferDetails? finalBest = refinedShort ?? bestShortWay;

            var longCandidate = refinedLong ?? bestLongWay;
            if (longCandidate is not null && (finalBest is null || longCandidate.DVTotal < finalBest.DVTotal))
            {
                finalBest = longCandidate;
            }

            if (finalBest is not null)
            {
                bestTransfer = finalBest;
            }
        }
        else
        {
            var refined = RefineBestTransfer(origin, destination, centralBody, request, window, bestTransfer);
            if (refined is not null && refined.DVTotal <= bestTransfer.DVTotal)
            {
                bestTransfer = refined;
            }
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

    /// <summary>
    /// Calculates transfer using both short-way and long-way Lambert solutions,
    /// returning the one with minimal total delta-V.
    /// If longWayOverride is specified, only that branch is evaluated.
    /// </summary>
    private static TransferDetails? CalculateBestTransfer(
        OrbitalBody origin,
        OrbitalBody destination,
        CentralBody centralBody,
        double departureTime,
        double travelTime,
        double departurePeriapsis,
        double? departureApoapsis,
        double? arrivalAltitude,
        string? arrivalMode,
        bool useAerobraking,
        bool? longWayOverride)
    {
        // If user explicitly specified longWay, use only that branch
        if (longWayOverride.HasValue)
        {
            try
            {
                return TransferCalculator.CalculateTransfer(
                    origin, destination, centralBody,
                    departureTime, travelTime,
                    departurePeriapsis, departureApoapsis,
                    arrivalAltitude, arrivalMode, useAerobraking,
                    longWayOverride);
            }
            catch
            {
                return null;
            }
        }

        // Otherwise, evaluate both branches and pick the best
        var candidates = new List<TransferDetails>();

        try
        {
            var shortTransfer = TransferCalculator.CalculateTransfer(
                origin, destination, centralBody,
                departureTime, travelTime,
                departurePeriapsis, departureApoapsis,
                arrivalAltitude, arrivalMode, useAerobraking,
                false);
            if (shortTransfer != null)
            {
                candidates.Add(shortTransfer);
            }
        }
        catch
        {
            // Short-way solution doesn't exist or failed
        }

        try
        {
            var longTransfer = TransferCalculator.CalculateTransfer(
                origin, destination, centralBody,
                departureTime, travelTime,
                departurePeriapsis, departureApoapsis,
                arrivalAltitude, arrivalMode, useAerobraking,
                true);
            if (longTransfer != null)
            {
                candidates.Add(longTransfer);
            }
        }
        catch
        {
            // Long-way solution doesn't exist or failed
        }

        return candidates
            .OrderBy(t => t.DVTotal)
            .FirstOrDefault();
    }

    private static TransferDetails? RefineBestTransfer(
        OrbitalBody origin,
        OrbitalBody destination,
        CentralBody centralBody,
        CalculationRequest request,
        PorkchopWindow window,
        TransferDetails seed)
    {
        var depMin = window.DepartureStart;
        var depMax = window.DepartureEnd;
        var ttMin = window.TravelTimeMin;
        var ttMax = window.TravelTimeMax;

        double Cost(double dep, double tt)
        {
            dep = Math.Clamp(dep, depMin, depMax);
            tt = Math.Clamp(tt, ttMin, ttMax);
            
            var t = CalculateBestTransfer(
                origin, destination, centralBody, dep, tt,
                request.ResolveDepartureOrbitPeriapsisAltitude(),
                request.ResolveDepartureOrbitApoapsisAltitude(),
                request.ArrivalParkingOrbitAltitude,
                request.ArrivalManeuverMode,
                request.UseAerobraking,
                request.LongWay);
            
            return t?.DVTotal ?? double.MaxValue;
        }

        var depStep = Math.Max((depMax - depMin) / 200.0, 3600.0);
        var ttStep = Math.Max((ttMax - ttMin) / 200.0, 3600.0);

        var simplex = new (double dep, double tt, double cost)[3];
        simplex[0] = (seed.DepartureTime, seed.TravelTime, Cost(seed.DepartureTime, seed.TravelTime));
        simplex[1] = (seed.DepartureTime + depStep, seed.TravelTime, Cost(seed.DepartureTime + depStep, seed.TravelTime));
        simplex[2] = (seed.DepartureTime, seed.TravelTime + ttStep, Cost(seed.DepartureTime, seed.TravelTime + ttStep));

        for (var iter = 0; iter < 80; iter++)
        {
            Array.Sort(simplex, (a, b) => a.cost.CompareTo(b.cost));
            var best = simplex[0];
            var good = simplex[1];
            var worst = simplex[2];

            if (worst.cost < double.MaxValue && Math.Abs(worst.cost - best.cost) < 1e-6)
            {
                break;
            }

            var cDep = (best.dep + good.dep) / 2.0;
            var cTt = (best.tt + good.tt) / 2.0;

            // Отражение
            var reflDep = cDep + (cDep - worst.dep);
            var reflTt = cTt + (cTt - worst.tt);
            var reflCost = Cost(reflDep, reflTt);

            if (reflCost < best.cost)
            {
                // Расширение
                var expDep = cDep + 2.0 * (cDep - worst.dep);
                var expTt = cTt + 2.0 * (cTt - worst.tt);
                var expCost = Cost(expDep, expTt);
                simplex[2] = expCost < reflCost ? (expDep, expTt, expCost) : (reflDep, reflTt, reflCost);
            }
            else if (reflCost < good.cost)
            {
                simplex[2] = (reflDep, reflTt, reflCost);
            }
            else
            {
                // Сжатие
                var contDep = cDep + 0.5 * (worst.dep - cDep);
                var contTt = cTt + 0.5 * (worst.tt - cTt);
                var contCost = Cost(contDep, contTt);
                if (contCost < worst.cost)
                {
                    simplex[2] = (contDep, contTt, contCost);
                }
                else
                {
                    // Уменьшение симплекса (shrink)
                    var midDep = (best.dep + good.dep) / 2.0;
                    var midTt = (best.tt + good.tt) / 2.0;
                    simplex[1] = (midDep, midTt, Cost(midDep, midTt));

                    var shrinkDep = (best.dep + worst.dep) / 2.0;
                    var shrinkTt = (best.tt + worst.tt) / 2.0;
                    simplex[2] = (shrinkDep, shrinkTt, Cost(shrinkDep, shrinkTt));
                }
            }
        }

        Array.Sort(simplex, (a, b) => a.cost.CompareTo(b.cost));
        var winner = simplex[0];
        if (winner.cost >= double.MaxValue)
        {
            return null;
        }

        // Final verification: check both branches at the winner point to ensure
        // we have the true best solution (in case simplex converged on one branch)
        var finalTransfer = CalculateBestTransfer(
            origin, destination, centralBody,
            Math.Clamp(winner.dep, depMin, depMax),
            Math.Clamp(winner.tt, ttMin, ttMax),
            request.ResolveDepartureOrbitPeriapsisAltitude(),
            request.ResolveDepartureOrbitApoapsisAltitude(),
            request.ArrivalParkingOrbitAltitude,
            request.ArrivalManeuverMode,
            request.UseAerobraking,
            request.LongWay);

        return finalTransfer;
    }
}
