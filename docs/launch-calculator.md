# Orbital Launch Delta-V Calculator (Расчёт дельта-V для подъёма на орбиту)

## Overview / Обзор

The integrated Orbital Launch Delta-V Calculator automatically computes the delta-V requirements for launching a rocket to orbit, including gravity losses calculations. Parameters are automatically substituted from the central body (planet) and target orbit specifications.

Интегрированный Калькулятор дельта-V для подъёма на орбиту автоматически вычисляет требуемый дельта-V для вывода ракеты на орбиту, включая расчёт гравитационных потерь. Параметры планеты и целевой орбиты подставляются автоматически.

## Features / Возможности

- ✅ **Automatic Parameter Substitution** - Планета, радиус и параметры орбиты подставляются автоматически
- ✅ **Gravity Losses Calculation** - Расчёт гравитационных потерь энергии
- ✅ **Gravity Turn Simulation** - Симуляция гравитационного поворота (Gravity Turn)
- ✅ **Multiple Output Metrics** - Множество выходных параметров (время, высота, скорость, потери)
- ✅ **Configurable Rocket Parameters** - Настраиваемые параметры ракеты
- ✅ **Works with Both Single and Porkchop Modes** - Работает в обоих режимах расчётов

## Usage / Использование

### JSON Configuration Example

```json
{
  "request": {
    "mode": "single",
    "origin": "Kerbin",
    "destination": "Mun",
    "departureTime": 0,
    "travelTime": 3600,
    "departureParkingOrbitAltitude": 70000,
    "arrivalParkingOrbitAltitude": 100000,
    "launch": {
      "enabled": true,
      "rocketInitialMass": 500000,
      "rocketThrust": 7000000,
      "rocketIsp": 310,
      "launchLatitude": 0,
      "targetInclination": 0,
      "planetRotationPeriod": 21549.425
    }
  }
}
```

### Launch Configuration Parameters / Параметры конфигурации запуска

| Parameter | Type | Description | Default |
|-----------|------|-------------|---------|
| `enabled` | bool | Enable launch calculations | `false` |
| `rocketInitialMass` | double | Rocket mass in kg | `500000` |
| `rocketThrust` | double | Thrust in Newtons | `7000000` |
| `rocketIsp` | double | Specific Impulse in seconds | `310` |
| `launchLatitude` | double | Launch latitude in degrees (0=equator) | `0.0` |
| `targetInclination` | double | Target orbit inclination in degrees | `0.0` |
| `planetRotationPeriod` | double? | Planet rotation period in seconds (optional) | `86400` |

## Output Example / Пример выходных данных

```
=== Расчёт подъёма на орбиту Kerbin ===
Целевая высота: 70.0 км
Целевой наклон: 0.0°

✓ УСПЕШНО
Время выведения: 345.2 сек (5.8 мин)
Итоговая высота: 70.23 км
Итоговая скорость: 2358.45 м/с

Энергетические затраты:
  Кинематический ΔV:      2210.42 м/с
  Фактический ΔV:         2758.87 м/с
  Гравитационные потери:  523.18 м/с
  Всего потерь:            523.18 м/с
  Эффективность:          80.1%
```

## Implementation Details / Детали реализации

### How It Works / Как это работает

1. **Planet Parameters** - Automatically extracted from the `OrbitalBody` object:
   - Gravitational parameter (μ = G × M)
   - Radius
   - Rotation period

2. **Target Orbit** - Automatically set to:
   - Periapsis: `DepartureParkingOrbitAltitude` from request
   - Apoapsis: Same as periapsis (circular orbit)
   - Inclination: From `launch.targetInclination`

3. **Rocket Parameters** - Configured via `LaunchConfiguration`

4. **Simulation** - Uses numerical integration (Euler method) with:
   - Gravity turn autopilot
   - Precise gravity loss calculation
   - Thrust and mass flow dynamics

### Integration Points / Точки интеграции

The calculator is integrated into `ScenarioExecutor.cs`:
- Launches after successful transfer calculation
- Works for both "single" and "porkchop" modes
- Appends results to console output

## Rocket Thrust-to-Weight Ratio Guide / Рекомендации по TWR

| Vehicle Type | TWR | Isp (vacuum) | Example |
|--------------|-----|-------------|---------|
| Falcon 9 Stage 1 | 1.4 | 282-310 s | Included in sample |
| Falcon 9 Stage 2 | 3.5+ | 348 s | - |
| SLS Block 2 | 2.0+ | 363 s | - |
| Saturn V | 1.15 | 265 s | - |

## Performance Notes / Замечания по производительности

- Simulation timestep: 0.1 seconds (configurable)
- Maximum simulation time: 1 hour
- Typical calculation time: < 1 second
- Numerical accuracy depends on timestep size

## Real-World Planet Data / Данные реальных планет

For Earth:
```json
"launch": {
  "enabled": true,
  "rocketInitialMass": 500000,
  "rocketThrust": 7000000,
  "rocketIsp": 310,
  "launchLatitude": 28.4,
  "targetInclination": 28.4,
  "planetRotationPeriod": 86164
}
```

For Kerbin (KSP):
```json
"launch": {
  "enabled": true,
  "rocketInitialMass": 500000,
  "rocketThrust": 7000000,
  "rocketIsp": 310,
  "launchLatitude": 0,
  "targetInclination": 0,
  "planetRotationPeriod": 21549.425
}
```

## Files Modified / Изменённые файлы

1. **OrbitalAscentCalculator.cs** (NEW) - Core calculator implementation
2. **InputModels.cs** - Added `LaunchConfiguration` class
3. **ScenarioExecutor.cs** - Integrated launch calculations
4. **examples/scenarios/launch/kerbin-to-mun-with-launch.json** - Example configuration

## Mathematical Background / Математическая основа

### Orbital Mechanics / Орбитальная механика

Target velocity at periapsis:
$$v_{target} = \sqrt{\mu \left( \frac{2}{r_{peri}} - \frac{1}{a} \right)}$$

Rotational velocity bonus:
$$v_{rot} = \omega R \cos(\phi)$$

### Gravity Losses / Гравитационные потери

Accumulated during ascent:
$$\Delta v_{gravity} = \int_0^t g \sin(\gamma) \, dt$$

Where γ is the flight path angle.

## Troubleshooting / Решение проблем

**Issue**: Launch fails to reach orbit
- **Solution**: Increase thrust, reduce mass, or reduce target altitude

**Issue**: Unrealistic gravity losses
- **Solution**: Check if `rocketIsp` and `rocketThrust` are realistic for the planet's surface gravity

**Issue**: Empty rotation period
- **Solution**: Leave as null (default 24 hours), or set to actual planet rotation period

## Future Enhancements / Возможные улучшения

- [ ] Atmospheric drag model
- [ ] Staged rocket support
- [ ] Arbitrary launch azimuth optimization
- [ ] Custom pitch profile
- [ ] Continuous thrust optimization
- [ ] Export trajectory data
