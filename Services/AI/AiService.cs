using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SpecMind.Models;
using SpecMind.Services.ReferenceMaterials;

namespace SpecMind.Services.AI
{
    public class AiService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly IReferenceMaterialService _referenceMaterialService;

        private const string LlmUrl = "https://api.openai.com/v1/chat/completions";
        private const string LlmModel = "gpt-4o-mini";

        public AiService(IReferenceMaterialService referenceMaterialService)
        {
            _referenceMaterialService = referenceMaterialService;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(120)
            };

            _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                      ?? throw new Exception("OPENAI_API_KEY не найден");
        }

        public async Task<AiAnalysisResult> AnalyzeTextAsync(string text, string analysisMode = "standard")
        {
            var templateText = await _referenceMaterialService.GetTemplateTextAsync();
            var exampleText = await _referenceMaterialService.GetExampleTextAsync();
            var criteriaText = await _referenceMaterialService.GetCriteriaTextAsync();

            var cleanText = NormalizeText(text);

            if (string.IsNullOrWhiteSpace(cleanText))
                throw new Exception("Текст документа пуст.");

            var templateFull = TrimForPrompt(templateText, 12000);
            var exampleFull = TrimForPrompt(exampleText, 12000);
            var criteriaFull = TrimForPrompt(criteriaText, 6000);

            if (cleanText.Length <= 45000)
            {
                var singleResult = await AnalyzeWholeDocumentAsync(
                    cleanText,
                    templateFull,
                    exampleFull,
                    criteriaFull);

                if (singleResult != null)
                {
                    singleResult.OriginalText = cleanText;
                    NormalizeAiResult(singleResult, cleanText);
                    EnforceBusinessRules(singleResult);
                    singleResult = await FillMissingExtractionsAsync(
    cleanText,
    singleResult,
    templateFull,
    exampleFull,
    criteriaFull);

                    var singleTooWeak =
                        singleResult.Criteria == null || !singleResult.Criteria.Any() ||
                        singleResult.Problems == null || !singleResult.Problems.Any() ||
                        singleResult.Recommendations == null || !singleResult.Recommendations.Any();

                    if (!singleTooWeak)
                    {
                        if (string.IsNullOrWhiteSpace(singleResult.ImprovedText))
                        {
                            singleResult.ImprovedText = await GenerateImprovedTextFromWholeAsync(
                                cleanText,
                                singleResult,
                                templateFull,
                                exampleFull);
                        }

                        if (string.IsNullOrWhiteSpace(singleResult.ImprovedText))
                            singleResult.ImprovedText = cleanText;

                        singleResult.ImprovedText = BeautifyDocumentText(singleResult.ImprovedText);

                        return singleResult;
                    }
                }
            }

            var templateShort = TrimForPrompt(templateText, 4000);
            var exampleShort = TrimForPrompt(exampleText, 3500);
            var criteriaShort = TrimForPrompt(criteriaText, 2500);

            var chunks = SplitIntoChunks(cleanText, 18000);

            if (chunks.Count == 0)
                throw new Exception("Не удалось подготовить текст документа для анализа.");

            var partialResults = new List<AiAnalysisResult>();

            foreach (var chunk in chunks)
            {
                var partial = await AnalyzeChunkAsync(
                    chunk,
                    templateShort,
                    exampleShort,
                    criteriaShort);

                partialResults.Add(partial);
            }

            var merged = MergeResults(partialResults, cleanText);
            NormalizeAiResult(merged, cleanText);
            EnforceBusinessRules(merged);
            merged = await FillMissingExtractionsAsync(
    cleanText,
    merged,
    templateFull,
    exampleFull,
    criteriaFull);
            var tooWeak =
                merged.Criteria == null || merged.Criteria.Count == 0 ||
                merged.Problems == null || merged.Problems.Count == 0 ||
                merged.Recommendations == null || merged.Recommendations.Count == 0;

            if (tooWeak)
            {
                var retryWhole = await AnalyzeWholeDocumentAsync(
                    TrimForPrompt(cleanText, 45000),
                    templateFull,
                    exampleFull,
                    criteriaFull);

                if (retryWhole != null)
                {
                    retryWhole.OriginalText = cleanText;
                    NormalizeAiResult(retryWhole, cleanText);
                    EnforceBusinessRules(retryWhole);
                    retryWhole = await FillMissingExtractionsAsync(
    cleanText,
    retryWhole,
    templateFull,
    exampleFull,
    criteriaFull);
                    var retryTooWeak =
                        retryWhole.Criteria == null || !retryWhole.Criteria.Any() ||
                        retryWhole.Problems == null || !retryWhole.Problems.Any() ||
                        retryWhole.Recommendations == null || !retryWhole.Recommendations.Any();

                    if (!retryTooWeak)
                    {
                        if (string.IsNullOrWhiteSpace(retryWhole.ImprovedText))
                        {
                            retryWhole.ImprovedText = await GenerateImprovedTextFromWholeAsync(
                                cleanText,
                                retryWhole,
                                templateFull,
                                exampleFull);
                        }

                        if (string.IsNullOrWhiteSpace(retryWhole.ImprovedText))
                            retryWhole.ImprovedText = cleanText;

                        retryWhole.ImprovedText = BeautifyDocumentText(retryWhole.ImprovedText);

                        return retryWhole;
                    }
                }
            }

            try
            {
                merged.ImprovedText = await GenerateImprovedTextFromWholeAsync(
                    cleanText,
                    merged,
                    templateFull,
                    exampleFull);
            }
            catch
            {
                merged.ImprovedText = cleanText;
            }

            if (string.IsNullOrWhiteSpace(merged.ImprovedText))
                merged.ImprovedText = cleanText;

            merged.ImprovedText = BeautifyDocumentText(merged.ImprovedText);
            NormalizeAiResult(merged, cleanText);
            EnforceBusinessRules(merged);

            return merged;
        }

        private async Task<AiAnalysisResult?> AnalyzeWholeDocumentAsync(
            string text,
            string templateText,
            string exampleText,
            string criteriaText)
        {
            var prompt = $$"""
Ты — строгий эксперт и редактор научно-технических заданий.

ТВОЯ ЗАДАЧА:
1. Проанализировать исходное НТЗ целиком.
2. Очень строго проверить соответствие шаблону.
3. Использовать example как эталон качественного заполнения.
4. Вернуть оценки по критериям.
5. Найти проблемы.
6. Дать рекомендации.
7. Проверить соответствие шаблону.
8. Выделить требования, сроки, KPI и ожидаемые результаты.

ОЧЕНЬ ВАЖНО:
- Анализируй ВЕСЬ документ целиком.
- Верни только валидный JSON.
- Не используй markdown fences.
- ШАБЛОН и EXAMPLE имеют очень высокий приоритет.
- Если структура документа расходится с шаблоном, обязательно укажи это.
- НЕ заполняй improvedText в этом JSON. improvedText должен быть пустой строкой "".
- В этом запросе нужен только анализ документа, без генерации новой полной версии текста.
- originalText тоже можно оставить пустой строкой "".

КРИТЕРИИ:
- Стратегическая релевантность (20)
- Цель и задачи (10)
- Научная новизна (15)
- Практическая применимость (20)
- Ожидаемые результаты (15)
- Социально-экономический эффект (10)
- Реализуемость (10)

ПРАВИЛА ОЦЕНКИ:
- overallScore = сумма всех criteria.score
- overallVerdict = "Проходит", если overallScore >= 60, иначе "Не проходит"
- passed = true, если критерий >= 60% от maxScore

ОЧЕНЬ ВАЖНЫЕ ПРАВИЛА ИЗВЛЕЧЕНИЯ:
- "Сроки" = только календарные этапы, периоды выполнения, годы/кварталы/месяцы выполнения работ, этапы реализации, даты начала и окончания.
- Сведения о финансировании, бюджете, суммах, "по годам в тыс. тенге", предельной сумме программы НЕ являются сроками.
- Если в документе нет явных этапов, периодов выполнения, календарного плана или сроков работ, то extractedDeadlines должен быть пустым массивом [].
- Никогда не превращай бюджет по годам в сроки.
- Если сроки отсутствуют, в problems и recommendations обязательно укажи это явно:
  - Проблема: "Не указаны сроки реализации программы и этапов выполнения работ."
  - Рекомендация: "Добавить отдельное описание сроков реализации программы и этапов выполнения работ."

ПРАВИЛА ДЛЯ COMMENT У КРИТЕРИЕВ:
Для каждого критерия comment обязан содержать:
1. Что именно найдено в документе.
2. Почему за это поставлен именно такой балл.
3. Чего конкретно не хватает до максимального балла.
4. Комментарий должен быть конкретным, а не абстрактным.
5. Нельзя писать общие фразы вроде "проект хороший" или "соответствует целям" без пояснения.

ОБЯЗАТЕЛЬНЫЕ ПРАВИЛА ИЗВЛЕЧЕНИЯ СУЩНОСТЕЙ:
- extractedRequirements: включай ключевые требования, обязательства, ожидаемые действия, технические и организационные положения из документа.
- extractedKpis: включай все количественные показатели, численные метрики, проценты, количества статей, патентов, публикаций, целевые значения и измеримые результаты.
- extractedExpectedResults: включай все прямые результаты, конечные результаты, эффекты, результаты внедрения, публикации, патенты, программные и организационные результаты.
- sectionClassifications: обязательно классифицируй каждый крупный раздел документа, если он явно присутствует.
- Если в документе есть раздел "4.1 Прямые результаты", extractedExpectedResults не должен быть пустым.
- Если в документе есть числовые показатели, проценты, количества публикаций, статей, патентов или целевых метрик, extractedKpis не должен быть пустым.
- Если в документе есть перечисление задач, требований или обязательных действий, extractedRequirements не должен быть пустым.
- Если в документе есть явные разделы 1, 2, 3, 4, 5, sectionClassifications не должен быть пустым.
- Не оставляй эти массивы пустыми, если информация в тексте явно присутствует.

ПРАВИЛА ДЛЯ summary:
- summary должен быть не общим пересказом, а содержательным выводом.
- Начинай summary с формулировки: "Данное НТЗ направлено на решение проблемы ..."
- Формулировка проблемы должна вытекать из самого текста документа.
- Сначала укажи, какую проблему решает НТЗ.
- Затем кратко покажи, как документ предлагает её решать.
- Затем укажи главные сильные стороны документа.
- Если есть недостатки, коротко обозначь их в конце summary.
- summary должен быть 3–5 предложений, а не одной короткой фразой.
- Не пиши слишком обобщено вроде "документ хороший" без пояснения.

ПРАВИЛА ДЛЯ problems И recommendations:
- problems должны быть конкретными и чуть более развёрнутыми.
- recommendations должны быть прикладными и объяснять, что именно нужно добавить или исправить.
- Избегай слишком коротких фраз из 3–5 слов.
- Каждая проблема и каждая рекомендация должны звучать как полноценная мысль.
- Если выявлена проблема отсутствия сроков, пиши подробнее:
  Проблема: "В документе не указаны сроки реализации программы, этапы выполнения работ и календарная последовательность выполнения задач, из-за чего снижается управляемость и проверяемость реализации НИР."
  Рекомендация: "Добавить отдельный блок со сроками реализации программы, этапами выполнения работ и календарной последовательностью достижения основных результатов по годам или этапам."

  СТИЛЬ ВЫВОДА:
- summary: 3–5 предложений
- problems: не менее 1 полного предложения на каждый пункт
- recommendations: не менее 1 полного предложения на каждый пункт
- Формулировки должны быть деловыми, экспертными и конкретными.
- Не использовать слишком короткие и сухие записи.

Пример хорошего comment:
"В документе приведены конкретные ссылки на стратегические документы и пункты, включая Концепцию развития ИИ и Национальный план развития, поэтому стратегическая привязка раскрыта хорошо. Балл снижен, поскольку не показано, как результаты программы будут прямо измеримо влиять на отдельные целевые индикаторы этих документов."

ШТАТНЫЙ ШАБЛОН:
{{templateText}}

ЭТАЛОННЫЙ ПРИМЕР:
{{exampleText}}

СПРАВОЧНЫЕ КРИТЕРИИ:
{{criteriaText}}

Верни СТРОГО JSON:
{
  "documentTitle": "string",
  "organization": "string",
  "expert": "AI Expert",
  "overallScore": 0,
  "overallVerdict": "Проходит",
  "summary": "string",
  "templateCompliance": {
    "matchesTemplate": true,
    "missingSections": ["string"],
    "structureComment": "string"
  },
  "criteria": [
    {
      "name": "Стратегическая релевантность",
      "weight": 20,
      "score": 0,
      "maxScore": 20,
      "passed": false,
      "comment": "string"
    },
    {
      "name": "Цель и задачи",
      "weight": 10,
      "score": 0,
      "maxScore": 10,
      "passed": false,
      "comment": "string"
    },
    {
      "name": "Научная новизна",
      "weight": 15,
      "score": 0,
      "maxScore": 15,
      "passed": false,
      "comment": "string"
    },
    {
      "name": "Практическая применимость",
      "weight": 20,
      "score": 0,
      "maxScore": 20,
      "passed": false,
      "comment": "string"
    },
    {
      "name": "Ожидаемые результаты",
      "weight": 15,
      "score": 0,
      "maxScore": 15,
      "passed": false,
      "comment": "string"
    },
    {
      "name": "Социально-экономический эффект",
      "weight": 10,
      "score": 0,
      "maxScore": 10,
      "passed": false,
      "comment": "string"
    },
    {
      "name": "Реализуемость",
      "weight": 10,
      "score": 0,
      "maxScore": 10,
      "passed": false,
      "comment": "string"
    }
  ],
  "problems": ["string"],
  "recommendations": ["string"],
  "sectionClassifications": [
    {
      "sectionName": "string",
      "category": "string",
      "comment": "string"
    }
  ],
  "extractedRequirements": ["string"],
  "extractedDeadlines": ["string"],
  "extractedKpis": ["string"],
  "extractedExpectedResults": ["string"],
  "originalText": "",
  "improvedText": ""
}

Текст НТЗ:
{{text}}
""";

            var json = await SendJsonPromptAsync(prompt, 0.05, 3500);
            var result = TryDeserializeAiResult(json);

            if (result == null)
            {
                var retryPrompt = prompt + "\n\nВерни ТОЛЬКО один валидный JSON-объект.";
                json = await SendJsonPromptAsync(retryPrompt, 0.0, 3500);
                result = TryDeserializeAiResult(json);
            }

            return result;
        }








        private async Task<string> GenerateImprovedTextFromWholeAsync(
            string fullText,
            AiAnalysisResult analysis,
            string templateText,
            string exampleText)
        {
            var prompt = $$"""
Ты — строгий редактор научно-технических заданий.

Нужно переписать документ в качественном, полном и аккуратно оформленном виде,
СТРОГО по шаблону и ориентируясь на пример заполнения.

ГЛАВНЫЕ ПРАВИЛА:
- Строго соблюдай структуру шаблона.
- Ориентируйся на example как на эталон качественного заполнения и форматирования.
- Сохраняй нормальные абзацы, переносы строк и читаемую структуру.
- Каждый раздел и подпункт начинай с новой строки.
- Не сливай весь документ в один абзац.
- Для перечислений используй нумерацию или маркированные списки там, где это уместно.
- Между крупными разделами оставляй пустую строку.
- Не добавляй лишние разделы, которых нет в шаблоне.
- Не выдумывай критичные факты.
- Если данных не хватает, указывай "(требует подтверждения)".
- Если сроки реализации программы не указаны явно, НЕ придумывай сроки.
- Бюджет по годам НЕ является сроками.
- Если явных сроков нет, НЕ создавай выдуманный календарный план.
- Улучшенный текст должен учитывать рекомендации анализа и исправлять найденные проблемы, если это можно сделать из имеющегося текста.
- Если проблема была выявлена и её можно устранить редактурой, структурированием и улучшением формулировок, обязательно устрани её.
- Если проблема требует новых исходных данных, которых нет в тексте, не выдумывай их и помечай "(требует подтверждения)".

ОБЯЗАТЕЛЬНО:
- Сначала внимательно проанализируй список проблем и рекомендаций.
- Затем при формировании improvedText исправь ВСЕ замечания, которые можно исправить редактурой, структурированием, нумерацией, логикой изложения, уточнением формулировок и более строгим соответствием шаблону.
- Если замечание можно устранить без выдумывания новых фактов, оно должно быть устранено в improvedText обязательно.
- Не оставляй найденные недочеты неисправленными, если они могут быть исправлены за счет более грамотного оформления и переработки текста.
- Если замечание требует новых исходных данных, которых нет в документе, не выдумывай их, а используй нейтральную формулировку с пометкой "(требует подтверждения)".
- improvedText должен быть лучше исходного текста именно по тем пунктам, которые были указаны в problems и recommendations.
- Если рекомендация касается структуры, логики, формулировок или читаемости, improvedText обязан это исправить.

ОСОБОЕ ТРЕБОВАНИЕ К ФОРМАТУ ВЫВОДА:
Верни обычный текст документа с переносами строк.
Не используй JSON.
Не используй markdown.
Не пиши пояснения до и после документа.

СТРУКТУРА ДОЛЖНА БЫТЬ ТАКОЙ:

Техническое задание
на научно-исследовательскую работу
в рамках программно-целевого финансирования

1. Общие сведения:
1.1. Наименование приоритета для научной, научно-технической программы (далее – программа):
...
1.2. Наименование специализированного направления программы:
...

2. Цели и задачи программы
2.1. Цель программы:
...
2.2. Для достижения поставленной цели должны быть решены следующие задачи:
1. ...
2. ...
3. ...

3. Какие пункты стратегических и программных документов решает (указать конкретные пункты):
1. ...
2. ...

4. Ожидаемые результаты
4.1 Прямые результаты:
1. ...
2. ...
...

4.2 Конечный результат:
...
Экономический эффект:
...
Экологический эффект:
...
Социальный эффект:
...
Целевыми потребителями полученных результатов являются:
...

5. Предельная сумма программы на весь срок реализации программы и по годам, в тыс. тенге:
...

ВАЖНО:
- Раздел 5 — это только финансирование.
- Не используй раздел 5 как источник сроков.
- Если в документе нет отдельного календарного плана, не вставляй выдуманные сроки.
- Если сроки отсутствуют, просто не добавляй выдуманный раздел сроков.
- Соблюдай стиль и структуру как в хорошем примере заполнения.

Проблемы анализа:
{{string.Join("\n", analysis.Problems ?? new List<string>())}}

Рекомендации анализа:
{{string.Join("\n", analysis.Recommendations ?? new List<string>())}}

Извлеченные требования:
{{string.Join("\n", analysis.ExtractedRequirements ?? new List<string>())}}

Извлеченные KPI:
{{string.Join("\n", analysis.ExtractedKpis ?? new List<string>())}}

Извлеченные ожидаемые результаты:
{{string.Join("\n", analysis.ExtractedExpectedResults ?? new List<string>())}}

Шаблон:
{{templateText}}

Пример заполнения:
{{exampleText}}

Исходный текст:
{{TrimForPrompt(fullText, 26000)}}

Верни только итоговый улучшенный текст документа с нормальными переносами строк и абзацами.
""";

            var raw = await SendPlainTextPromptAsync(prompt, 0.1, 5000);
            return BeautifyDocumentText(raw);
        }

        public async Task<string> AnswerChatAsync(string userMessage, string? contextText = null)
        {
            var templateText = await _referenceMaterialService.GetTemplateTextAsync();
            var criteriaText = await _referenceMaterialService.GetCriteriaTextAsync();

            templateText = TrimForPrompt(templateText, 1200);
            criteriaText = TrimForPrompt(criteriaText, 900);
            contextText = TrimForPrompt(contextText, 3200);
            userMessage = TrimForPrompt(userMessage, 2500);

            var hasContext = !string.IsNullOrWhiteSpace(contextText);

            var prompt = hasContext
                ? $$"""
Ты — AI-помощник по научным техническим заданиям.

ПРАВИЛА ОТВЕТА:
- Отвечай быстро, понятно и по делу.
- Если вопрос простой — отвечай коротко.
- Если пользователь просит оформить, улучшить или объяснить НТЗ — используй контекст.
- Можно использовать markdown: **жирный**, списки, короткие подзаголовки.
- Не пиши огромные вступления.

Шаблон НТЗ:
{{templateText}}

Критерии:
{{criteriaText}}

Контекст:
{{contextText}}

Сообщение пользователя:
{{userMessage}}
"""
                : $$"""
Ты — AI-помощник по научным техническим заданиям.

ПРАВИЛА ОТВЕТА:
- Пользователь сейчас не загрузил НТЗ.
- Не делай вид, что документ уже у тебя есть.
- Если вопрос простой, например "привет", отвечай естественно и очень быстро.
- Если вопрос по НТЗ, шаблону, критериям — объясняй понятно.
- Можно использовать markdown: **жирный**, списки, короткие подзаголовки.
- Не пиши огромные вступления.

Шаблон НТЗ:
{{templateText}}

Критерии:
{{criteriaText}}

Сообщение пользователя:
{{userMessage}}
""";

            return await SendPlainTextPromptAsync(prompt, 0.3, 900);
        }

        private async Task<AiAnalysisResult> AnalyzeChunkAsync(
            string textChunk,
            string templateText,
            string exampleText,
            string criteriaText)
        {
            var prompt = $$"""
Ты — строгий эксперт по научно-техническим заданиям.

Проанализируй ТОЛЬКО этот фрагмент документа и верни JSON.

НУЖНО:
1. Найти проблемы.
2. Дать рекомендации.
3. Оценить релевантные критерии.
4. Проверить соответствие шаблону.
5. Классифицировать разделы.
6. Выделить требования.
7. Выделить сроки.
8. Выделить KPI.
9. Выделить ожидаемые результаты.
10. Сделать краткое summary по фрагменту.

ПРАВИЛА:
- Если какие-то элементы в этом фрагменте не встречаются, возвращай пустые списки.
- Не выдумывай данные, которых нет.
- Если есть явный намёк, но точность низкая — добавляй формулировку с пометкой "(требует подтверждения)".
- Не генерируй improvedText здесь.
- НЕ заполняй improvedText в этом JSON. improvedText должен быть пустой строкой "".
- originalText можно оставить пустой строкой "".
- Верни только валидный JSON-объект.
- Не используй markdown fences.
- Все массивы и объекты должны быть закрыты корректно.
- Бюджет по годам и суммы в тенге не являются сроками.
- Если сроков в этом фрагменте нет, extractedDeadlines должен быть [].

Критерии:
- Стратегическая релевантность (20)
- Цель и задачи (10)
- Научная новизна (15)
- Практическая применимость (20)
- Ожидаемые результаты (15)
- Социально-экономический эффект (10)
- Реализуемость (10)

Шаблон:
{{templateText}}

Пример:
{{exampleText}}

Критерии и пояснения:
{{criteriaText}}

Верни СТРОГО JSON:
{
  "documentTitle": "",
  "organization": "",
  "expert": "AI Expert",
  "overallScore": 0,
  "overallVerdict": "",
  "summary": "",
  "templateCompliance": {
    "matchesTemplate": false,
    "missingSections": [],
    "structureComment": ""
  },
  "criteria": [],
  "problems": [],
  "recommendations": [],
  "sectionClassifications": [],
  "extractedRequirements": [],
  "extractedDeadlines": [],
  "extractedKpis": [],
  "extractedExpectedResults": [],
  "originalText": "",
  "improvedText": ""
}

Фрагмент документа:
{{textChunk}}
""";

            var json = await SendJsonPromptAsync(prompt, 0.05, 2200);
            var result = TryDeserializeAiResult(json);

            if (result == null)
            {
                var retryPrompt = prompt + "\n\nНапоминаю: верни ТОЛЬКО один валидный JSON-объект без пояснений.";
                json = await SendJsonPromptAsync(retryPrompt, 0.0, 2200);
                result = TryDeserializeAiResult(json);
            }

            if (result == null)
            {
                result = new AiAnalysisResult
                {
                    Summary = "",
                    Problems = new List<string>(),
                    Recommendations = new List<string>(),
                    Criteria = new List<CriterionResult>(),
                    TemplateCompliance = new TemplateComplianceResult
                    {
                        MatchesTemplate = false,
                        MissingSections = new List<string>(),
                        StructureComment = ""
                    },
                    SectionClassifications = new List<SectionClassificationResult>(),
                    ExtractedRequirements = new List<string>(),
                    ExtractedDeadlines = new List<string>(),
                    ExtractedKpis = new List<string>(),
                    ExtractedExpectedResults = new List<string>(),
                    OriginalText = "",
                    ImprovedText = ""
                };
            }

            NormalizeAiResult(result, textChunk);
            EnforceBusinessRules(result);
            return result;
        }

        private static AiAnalysisResult? TryDeserializeAiResult(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            json = json.Trim();

            if (json.StartsWith("```"))
            {
                var firstBrace = json.IndexOf('{');
                var lastBrace = json.LastIndexOf('}');
                if (firstBrace >= 0 && lastBrace > firstBrace)
                    json = json.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            try
            {
                return JsonSerializer.Deserialize<AiAnalysisResult>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return null;
            }
        }

        private AiAnalysisResult MergeResults(List<AiAnalysisResult> partials, string originalText)
        {
            var merged = new AiAnalysisResult
            {
                OriginalText = originalText,
                DocumentTitle = partials.Select(x => x.DocumentTitle).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "",
                Organization = partials.Select(x => x.Organization).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "",
                Expert = "AI Expert",
                Summary = string.Join(" ", partials.Select(x => x.Summary).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()),
                Problems = partials.SelectMany(x => x.Problems ?? new List<string>()).Distinct().Take(30).ToList(),
                Recommendations = partials.SelectMany(x => x.Recommendations ?? new List<string>()).Distinct().Take(30).ToList(),
                SectionClassifications = partials
                    .SelectMany(x => x.SectionClassifications ?? new List<SectionClassificationResult>())
                    .GroupBy(x => $"{x.SectionName}|{x.Category}|{x.Comment}")
                    .Select(g => g.First())
                    .Take(40)
                    .ToList(),
                ExtractedRequirements = partials.SelectMany(x => x.ExtractedRequirements ?? new List<string>()).Distinct().Take(40).ToList(),
                ExtractedDeadlines = partials.SelectMany(x => x.ExtractedDeadlines ?? new List<string>()).Distinct().Take(30).ToList(),
                ExtractedKpis = partials.SelectMany(x => x.ExtractedKpis ?? new List<string>()).Distinct().Take(30).ToList(),
                ExtractedExpectedResults = partials.SelectMany(x => x.ExtractedExpectedResults ?? new List<string>()).Distinct().Take(30).ToList()
            };

            var groupedCriteria = partials
                .SelectMany(x => x.Criteria ?? new List<CriterionResult>())
                .GroupBy(x => x.Name)
                .Select(g =>
                {
                    var valid = g.Where(x => x.Score > 0).ToList();
                    var maxScore = g.Max(x => x.MaxScore);
                    var weight = g.Max(x => x.Weight);
                    var bestScore = valid.Any() ? valid.Max(x => x.Score) : 0;
                    var comments = string.Join(" ", g.Select(x => x.Comment).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());

                    return new CriterionResult
                    {
                        Name = g.Key,
                        Weight = weight,
                        MaxScore = maxScore,
                        Score = Math.Min(bestScore, maxScore),
                        Passed = bestScore >= Math.Ceiling(maxScore * 0.6),
                        Comment = comments
                    };
                })
                .ToList();

            merged.Criteria = groupedCriteria;
            merged.TotalizeTemplateCompliance(partials);
            merged.OverallScore = merged.Criteria.Sum(x => x.Score);
            merged.OverallVerdict = merged.OverallScore >= 60 ? "Проходит" : "Не проходит";

            NormalizeAiResult(merged, originalText);
            EnforceBusinessRules(merged);

            return merged;
        }

        private async Task<string> SendJsonPromptAsync(string prompt, double temperature, int maxTokens)
        {
            var responseString = await SendRawAsync(prompt, temperature, maxTokens, true);

            using var jsonDoc = JsonDocument.Parse(responseString);

            var content = jsonDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
                throw new Exception("AI вернул пустой JSON-ответ.");

            return CleanupJson(content);
        }

        private async Task<string> SendPlainTextPromptAsync(string prompt, double temperature, int maxTokens)
        {
            var responseString = await SendRawAsync(prompt, temperature, maxTokens, false);

            using var jsonDoc = JsonDocument.Parse(responseString);

            var content = jsonDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(content) ? "" : content.Trim();
        }

        private async Task<string> SendRawAsync(string prompt, double temperature, int maxTokens, bool forceJson)
        {
            object requestBody;

            if (forceJson)
            {
                requestBody = new
                {
                    model = LlmModel,
                    messages = new object[]
                    {
                        new { role = "system", content = "Возвращай только один валидный JSON-объект без пояснений и без markdown." },
                        new { role = "user", content = prompt }
                    },
                    temperature = temperature,
                    max_tokens = maxTokens,
                    response_format = new { type = "json_object" }
                };
            }
            else
            {
                requestBody = new
                {
                    model = LlmModel,
                    messages = new object[]
                    {
                        new { role = "user", content = prompt }
                    },
                    temperature = temperature,
                    max_tokens = maxTokens
                };
            }

            var requestJson = JsonSerializer.Serialize(requestBody);

            using var request = new HttpRequestMessage(HttpMethod.Post, LlmUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Ошибка OpenAI: {responseString}");

            return responseString;
        }

        private static void NormalizeAiResult(AiAnalysisResult result, string originalText)
        {
            result.DocumentTitle ??= "";
            result.Organization ??= "";
            result.Expert ??= "AI Expert";
            result.Summary ??= "";
            result.Problems ??= new List<string>();
            result.Recommendations ??= new List<string>();
            result.Criteria ??= new List<CriterionResult>();
            result.TemplateCompliance ??= new TemplateComplianceResult();
            result.TemplateCompliance.MissingSections ??= new List<string>();
            result.TemplateCompliance.StructureComment ??= "";
            result.SectionClassifications ??= new List<SectionClassificationResult>();
            result.ExtractedRequirements ??= new List<string>();
            result.ExtractedDeadlines ??= new List<string>();
            result.ExtractedKpis ??= new List<string>();
            result.ExtractedExpectedResults ??= new List<string>();

            foreach (var criterion in result.Criteria)
            {
                criterion.Name ??= "";
                criterion.Comment ??= "";
            }

            foreach (var section in result.SectionClassifications)
            {
                section.SectionName ??= "";
                section.Category ??= "";
                section.Comment ??= "";
            }

            if (string.IsNullOrWhiteSpace(result.OriginalText))
                result.OriginalText = originalText;

            if (string.IsNullOrWhiteSpace(result.Summary))
                result.Summary = "Анализ документа выполнен, но часть полей была заполнена неполно.";

            result.OverallScore = result.Criteria.Sum(x => x.Score);
            result.OverallVerdict = result.OverallScore >= 60 ? "Проходит" : "Не проходит";
        }

        private static void EnforceBusinessRules(AiAnalysisResult result)
        {
            result.Problems ??= new List<string>();
            result.Recommendations ??= new List<string>();
            result.ExtractedDeadlines ??= new List<string>();
            result.ExtractedRequirements ??= new List<string>();
            result.ExtractedKpis ??= new List<string>();
            result.ExtractedExpectedResults ??= new List<string>();

            result.ExtractedDeadlines = result.ExtractedDeadlines
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x) &&
                    !x.Contains("тенге", StringComparison.OrdinalIgnoreCase) &&
                    !x.Contains("тыс.", StringComparison.OrdinalIgnoreCase) &&
                    !x.Contains("бюдж", StringComparison.OrdinalIgnoreCase) &&
                    !x.Contains("сумм", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            if (!result.ExtractedDeadlines.Any())
            {
                AddIfMissing(
                    result.Problems,
                    "В документе не указаны сроки реализации программы, этапы выполнения работ и календарная последовательность достижения результатов, из-за чего снижается управляемость, проверяемость и практическая реализуемость НИР."
                );

                AddIfMissing(
                    result.Recommendations,
                    "Добавить отдельный блок со сроками реализации программы, этапами выполнения работ и календарной последовательностью достижения основных результатов по годам или этапам выполнения."
                );
            }
        }

        private static void AddIfMissing(List<string> list, string value)
        {
            if (!list.Any(x => string.Equals(x?.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase)))
                list.Add(value);
        }

        private static string BeautifyDocumentText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = text.Replace("\r\n", "\n").Trim();

            normalized = normalized.Replace(
                "Техническое задание на научно-исследовательскую работу в рамках программно-целевого финансирования",
                "Техническое задание\nна научно-исследовательскую работу\nв рамках программно-целевого финансирования");

            var markers = new[]
            {
        "1. Общие сведения",
        "1.1. Наименование приоритета",
        "1.2. Наименование специализированного направления",
        "2. Цели и задачи программы",
        "2.1. Цель программы",
        "2.2. Для достижения поставленной цели должны быть решены следующие задачи",
        "3. Какие пункты стратегических и программных документов решает",
        "4. Ожидаемые результаты",
        "4.1 Прямые результаты",
        "4.2 Конечный результат",
        "Экономический эффект:",
        "Экологический эффект:",
        "Социальный эффект:",
        "Целевыми потребителями полученных результатов",
        "5. Предельная сумма программы"
    };

            foreach (var marker in markers)
            {
                normalized = normalized.Replace(marker, $"\n\n{marker}");
            }

            while (normalized.Contains("\n\n\n"))
                normalized = normalized.Replace("\n\n\n", "\n\n");

            normalized = normalized.Replace("\n\n\n", "\n\n").Trim();

            return normalized;
        }

        private static string NormalizeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Replace("\r\n", "\n").Trim();
        }

        private static List<string> SplitIntoChunks(string text, int maxChunkLength)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            if (text.Length <= maxChunkLength)
            {
                result.Add(text);
                return result;
            }

            var paragraphs = text.Split("\n\n", StringSplitOptions.None);
            var current = new StringBuilder();

            foreach (var paragraph in paragraphs)
            {
                if (current.Length + paragraph.Length + 2 <= maxChunkLength)
                {
                    if (current.Length > 0)
                        current.Append("\n\n");
                    current.Append(paragraph);
                }
                else
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }

                    if (paragraph.Length <= maxChunkLength)
                    {
                        current.Append(paragraph);
                    }
                    else
                    {
                        for (int i = 0; i < paragraph.Length; i += maxChunkLength)
                        {
                            var len = Math.Min(maxChunkLength, paragraph.Length - i);
                            result.Add(paragraph.Substring(i, len));
                        }
                    }
                }
            }

            if (current.Length > 0)
                result.Add(current.ToString());

            return result;
        }

        private static string TrimForPrompt(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value.Trim();
            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private static string CleanupJson(string content)
        {
            content = content.Trim();

            if (content.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                content = content[7..].Trim();
            else if (content.StartsWith("```"))
                content = content[3..].Trim();

            if (content.EndsWith("```"))
                content = content[..^3].Trim();

            var firstBrace = content.IndexOf('{');
            var lastBrace = content.LastIndexOf('}');

            if (firstBrace >= 0 && lastBrace > firstBrace)
                content = content.Substring(firstBrace, lastBrace - firstBrace + 1);

            return content;
        }


        private async Task<AiAnalysisResult> FillMissingExtractionsAsync(
    string cleanText,
    AiAnalysisResult result,
    string templateText,
    string exampleText,
    string criteriaText)
        {
            var needSections = result.SectionClassifications == null || !result.SectionClassifications.Any();
            var needRequirements = result.ExtractedRequirements == null || !result.ExtractedRequirements.Any();
            var needKpis = result.ExtractedKpis == null || !result.ExtractedKpis.Any();
            var needExpected = result.ExtractedExpectedResults == null || !result.ExtractedExpectedResults.Any();

            if (!needSections && !needRequirements && !needKpis && !needExpected)
                return result;

            var prompt = $$"""
Ты — эксперт по разметке научно-технических заданий.

Нужно ДОПОЛНИТЕЛЬНО извлечь сущности из документа.
Не оценивай документ заново.
Не пиши improvedText.
Нужны только sectionClassifications, extractedRequirements, extractedKpis, extractedExpectedResults.

ПРАВИЛА:
- sectionClassifications: классифицируй все явные крупные разделы документа.
- extractedRequirements: извлекай задачи, требования, обязательные действия, технические и организационные положения.
- extractedKpis: извлекай количественные показатели, проценты, целевые значения, числа публикаций, статьи, патенты, метрики и измеримые результаты.
- extractedExpectedResults: извлекай прямые результаты, конечные результаты, эффекты, результаты внедрения, публикации, патенты и иные результативные положения.
- Если в тексте это явно есть, массивы не должны быть пустыми.
- Верни только валидный JSON.

Шаблон:
{{templateText}}

Пример:
{{exampleText}}

Критерии:
{{criteriaText}}

Документ:
{{TrimForPrompt(cleanText, 30000)}}

Верни СТРОГО JSON:
{
  "sectionClassifications": [
    {
      "sectionName": "string",
      "category": "string",
      "comment": "string"
    }
  ],
  "extractedRequirements": ["string"],
  "extractedKpis": ["string"],
  "extractedExpectedResults": ["string"]
}
""";

            var json = await SendJsonPromptAsync(prompt, 0.0, 2200);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (needSections && root.TryGetProperty("sectionClassifications", out var secEl) && secEl.ValueKind == JsonValueKind.Array)
                {
                    result.SectionClassifications = JsonSerializer.Deserialize<List<SectionClassificationResult>>(secEl.GetRawText(), new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<SectionClassificationResult>();
                }

                if (needRequirements && root.TryGetProperty("extractedRequirements", out var reqEl) && reqEl.ValueKind == JsonValueKind.Array)
                {
                    result.ExtractedRequirements = JsonSerializer.Deserialize<List<string>>(reqEl.GetRawText()) ?? new List<string>();
                }

                if (needKpis && root.TryGetProperty("extractedKpis", out var kpiEl) && kpiEl.ValueKind == JsonValueKind.Array)
                {
                    result.ExtractedKpis = JsonSerializer.Deserialize<List<string>>(kpiEl.GetRawText()) ?? new List<string>();
                }

                if (needExpected && root.TryGetProperty("extractedExpectedResults", out var expEl) && expEl.ValueKind == JsonValueKind.Array)
                {
                    result.ExtractedExpectedResults = JsonSerializer.Deserialize<List<string>>(expEl.GetRawText()) ?? new List<string>();
                }
            }
            catch
            {
                
            }

            result.SectionClassifications ??= new List<SectionClassificationResult>();
            result.ExtractedRequirements ??= new List<string>();
            result.ExtractedKpis ??= new List<string>();
            result.ExtractedExpectedResults ??= new List<string>();

            return result;
        }
    }

    internal static class AiAnalysisMergeExtensions
    {
        public static void TotalizeTemplateCompliance(this AiAnalysisResult merged, List<AiAnalysisResult> partials)
        {
            var allMissing = partials
                .SelectMany(x => x.TemplateCompliance?.MissingSections ?? new List<string>())
                .Distinct()
                .Take(30)
                .ToList();

            var structureComment = string.Join(" ",
                partials.Select(x => x.TemplateCompliance?.StructureComment)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct());

            var matchesTemplate = partials.Any() && partials.All(x => x.TemplateCompliance?.MatchesTemplate == true);

            merged.TemplateCompliance = new TemplateComplianceResult
            {
                MatchesTemplate = matchesTemplate,
                MissingSections = allMissing,
                StructureComment = structureComment
            };
        }
    }
}