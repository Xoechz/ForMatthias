# Configuration

## Resolution

For each physical source file, search ancestors for `.csharp-organizer.json`. The nearest `root: true` stops organizer inheritance. Apply outermost to innermost above built-in defaults. Objects merge recursively; scalars override; arrays replace completely (including `[]`). `exclusions` is the exception: inherited patterns accumulate without duplicates.

`--config path.json` supplies a complete organizer configuration over defaults and bypasses all directory organizer configs. It does not bypass `.editorconfig`. Every layer is validated against the embedded version-1 Draft 2020-12 schema before any source transformation. Unknown properties, invalid enum values/ranges, duplicate rule IDs, unsafe region names and empty match arrays are errors with filename and JSON path.

```json
{
  "$schema": "./config.schema.json",
  "schemaVersion": 1,
  "root": true,
  "organization": {
    "rules": [
      {
        "id": "public-async",
        "match": {
          "kind": ["method"],
          "accessibility": ["public"],
          "modifiersAll": ["static"],
          "nameGlob": "*Async"
        },
        "sortBy": ["name", "signature"],
        "region": "Public Static Async Methods"
      },
      { "id": "remaining", "sortBy": [], "region": "Remaining Members" }
    ]
  },
  "wrapping": { "maxLineLength": 100 },
  "exclusions": ["**/Legacy/**"]
}
```

This replaces the entire default rules array. The first matching rule wins. Predicates are AND-combined; values within kind/accessibility/type-kind arrays are OR-combined. `modifiersAll`, `modifiersAny`, `modifiersNone`, `containingTypeKinds`, `nameGlob` and `explicitInterface` are supported. Simple-name glob matching is ordinal/case-sensitive. IDs must be unique and region names cannot contain newline or `#` characters. Unmatched declarations follow configured groups in original relative order.

## Sections

The full defaults and accepted keys are in the companion [sample](../sample-config.json) and [schema](../config.schema.json).

- `organization.enabled`: member sorting. `sortBy` defaults to accessibility, static, name, signature. A rule may override the array; `[]` preserves order. `original` terminates key comparison. Original source index is always the final tie-breaker.
- `regions`: regenerate direct-member organizational regions. `minMembers` defaults to **1**; `emitForUnmatched` defaults true. Noncontiguous runs may have the same region name. Body-local and whole-type outer regions are preserved.
- `wrapping`: threshold-triggered lists; nullable `maxLineLength` means EditorConfig `max_line_length`, otherwise 120. The sample explicitly chooses 120. Tabs expand using `tab_width` (falling back to `indent_size`); Unicode scalar values count as one column. Continuation indentation defaults to one level. Closing `)` is on the final item line.
- `wrapping.targets`: calls (including delegates), method/constructor/local-function declarations, constructor initializers, record primary constructors and parenthesized lambdas. Attribute, indexer and tuple targets are deliberately rejected if enabled. Method and delegate invocation switches are independent: project mode resolves the bound method kind in every applicable context. If the switches differ and binding is unavailable or inconsistent (including syntax-only mode), the ambiguous overflowing list is preserved with `CSORG4002`; `--fail-on-skipped` applies. Equal switches need no semantic classification.
- `format`: Roslyn's standard formatter plus direct-member blank-line counts. Fields default to zero blank lines, methods/properties/constructors/groups to one, type-brace boundaries to zero. Comments/directives/raw data are protected. Existing multiline list **breakpoints** are preserved when `preserveExistingMultiline` is true; spacing and indentation inside lists are still formatted. List-item continuation indentation follows the configured indentation levels.
- `cleanup`: scoped import organization, trivia-safe duplicates, project-only compiler-proven unused imports and safe trailing whitespace. No expression simplification or other refactoring.
- `exclusions`: additive path globs, relative to the selection root (filename-only patterns also match basenames). `--include` and `--exclude` use the same matching. Generated-name/header/output filters apply by default; `--include-generated` opts in while retaining explicit excludes.

## EditorConfig

Roslyn analyzer-config documents supply standard `csharp_*` formatting settings. Indentation, tab width, newline preference, final newline and custom maximum width also resolve per file. EditorConfig hierarchy, `root`, matching sections, `unset`, glob wildcards, character classes, alternatives and bounded numeric ranges are supported by the custom-setting bridge. Roslyn independently resolves its own standard settings.

Whitespace-significant literal tokens are preserved. UTF-8 with/without BOM and BOM-marked UTF-16 LE/BE are preserved. Unsupported/unreliably decoded encodings reject writes. Without explicit newline preference, generated whitespace uses the dominant source convention; untouched text is retained, including mixed endings. Full formatting may change whitespace throughout its selected document; selective commands retain unrelated formatting.

## Selective execution

`--only organize` changes ordering, managed regions and direct-member separators without formatting method bodies. `--only wrap` targets eligible lists only. `--only cleanup` changes imports and safe trailing whitespace. `--only format` applies standard formatting and direct-member blank-line policy without sorting or generating regions. JSON enabled switches remain effective within the requested capability set.
