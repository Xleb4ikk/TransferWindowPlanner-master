# Инструкции по тестированию улучшений

## Быстрый тест

### 1. Сборка проекта
```bash
dotnet build
```

### 2. Запуск автоматического режима (проверка обеих веток)
```bash
dotnet run --project StandaloneTrajectoryCalculator test-scenario.json
```

**Ожидаемый результат:**
- Long-way transfer: yes
- Total dV: ~5766 m/s
- Departure: 2026-10-25

### 3. Запуск с явным short-way
```bash
dotnet run --project StandaloneTrajectoryCalculator test-scenario-short.json
```

**Ожидаемый результат:**
- Long-way transfer: no
- Total dV: ~5869 m/s
- Departure: 2026-11-12

### 4. Запуск с явным long-way
```bash
dotnet run --project StandaloneTrajectoryCalculator test-scenario-long.json
```

**Ожидаемый результат:**
- Long-way transfer: yes
- Total dV: ~5766 m/s
- Departure: 2026-10-25

## Интерпретация результатов

### ✅ Успешный тест
Если автоматический режим даёт **5766 m/s**, а short-way даёт **5869 m/s**, то:
- Алгоритм корректно проверил обе ветви
- Выбрал более оптимальную (long-way)
- Экономия составила 102.4 m/s

### ❌ Если автоматический режим даёт 5869 m/s
Это означало бы, что алгоритм выбрал short-way, хотя long-way лучше. Это указывало бы на проблему в реализации.

## Тестирование на своих данных

### Создание нового сценария
```bash
dotnet run --project StandaloneTrajectoryCalculator template porkchop my-scenario.json
```

Отредактируйте `my-scenario.json`:
- Измените `origin` и `destination`
- Настройте окна вылета и время перелёта
- Установите параметры орбит

### Запуск с автоматическим выбором ветви
```json
{
  "request": {
    "mode": "porkchop",
    ...
    // НЕ указывайте "longWay" — будет автоматический режим
  }
}
```

### Принудительный short-way
```json
{
  "request": {
    "mode": "porkchop",
    ...
    "longWay": false
  }
}
```

### Принудительный long-way
```json
{
  "request": {
    "mode": "porkchop",
    ...
    "longWay": true
  }
}
```

## Проверка производительности

Для сетки 48×48 (2304 точки):
- **До изменений:** ~3-4 секунды
- **После изменений:** ~6-7 секунд
- **Увеличение:** ~2x (ожидаемо)

Для сетки 80×80 (6400 точек):
- **Ожидаемое время:** ~15-20 секунд

## Сравнение с предыдущей версией

### Откат к предыдущей версии (для сравнения)
```bash
git stash
dotnet build
dotnet run --project StandaloneTrajectoryCalculator test-scenario.json
```

**Сохраните результат Total dV!**

### Возврат к новой версии
```bash
git stash pop
dotnet build
dotnet run --project StandaloneTrajectoryCalculator test-scenario.json
```

**Сравните результаты.**

Если новая версия даёт **меньший или равный** Total dV, это подтверждает, что улучшение работает корректно.

## Визуальная проверка (GUI)

### Запуск WPF приложения
```bash
dotnet run --project TransferWindowPlanner.Wpf
```

1. Выберите Origin: Earth
2. Выберите Destination: Mars
3. Установите окно вылета: 2026-08-01
4. Нажмите "Calculate Porkchop"
5. Проверьте результат:
   - Best transfer должен показывать Long-way
   - Total dV должен быть ~5766 m/s

## Отладка

### Включение подробного вывода
Если нужно увидеть, какие ветви проверяются, добавьте логирование в `CalculateBestTransfer()`:

```csharp
Console.WriteLine($"Short-way: {shortTransfer?.DVTotal ?? double.NaN} m/s");
Console.WriteLine($"Long-way: {longTransfer?.DVTotal ?? double.NaN} m/s");
Console.WriteLine($"Selected: {candidates.FirstOrDefault()?.LongWay}");
```

### Проверка конкретной точки
Создайте single-transfer сценарий для детальной проверки:

```bash
dotnet run --project StandaloneTrajectoryCalculator template single my-single.json
```

Отредактируйте `my-single.json` с конкретными датой вылета и временем перелёта.

## Контрольные точки

- [ ] Сборка проходит без ошибок
- [ ] Автоматический режим выбирает Long-way (5766 m/s)
- [ ] Явный short-way даёт 5869 m/s
- [ ] Явный long-way даёт 5766 m/s
- [ ] Время выполнения увеличилось примерно в 2 раза
- [ ] GUI приложение запускается и показывает корректные результаты

## Известные ограничения

- Для очень эксцентричных орбит автоматическое определение ветви может работать некорректно
- Для миссий с гравитационными манёврами результат может быть неоптимальным
- Multi-revolution траектории пока не поддерживаются

## Вопросы?

Если результаты отличаются от ожидаемых, проверьте:
1. Версия .NET (требуется .NET 10.0)
2. Корректность JSON файлов
3. Все ли файлы из коммита включены
4. Чистая пересборка: `dotnet clean && dotnet build`
