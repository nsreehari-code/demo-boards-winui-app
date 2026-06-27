using System;
using System.Collections.Generic;
using System.Linq;
using DemoBoards_WinUI.Controls.Shared;

namespace ConverterHarness;

// Parity harness for the plain-data -> typed-record converters that back the
// shared board components. Each component now takes plain data props and calls
// one of these FromData converters internally; this asserts the conversions
// preserve every data-driven field the frontend authors.
//
// It also exercises the framework-agnostic registry/lib helpers that live in
// DemoBoards.Shared (path / bind / coerce / threshold / chart / fieldConfig /
// json) so they are covered headlessly here, without booting a WinUI host.
internal static class Program
{
    private static int Main()
    {
        var checks = new List<(string Name, Func<bool> Run)>();

        // ---- FieldSchema.FromData ------------------------------------------------
        checks.Add(("FieldSchema maps scalar fields", () =>
        {
            var schema = FieldSchema.FromData(Map(
                ("type", "number"), ("title", "Age"), ("placeholder", "yrs"),
                ("minimum", 1), ("maximum", 120), ("colSpan", 2), ("rows", 4),
                ("multiline", true), ("readOnly", true), ("disabled", true)));
            return schema.Type == "number"
                && schema.Title == "Age"
                && schema.Placeholder == "yrs"
                && schema.Minimum == 1
                && schema.Maximum == 120
                && schema.ColSpan == 2
                && schema.Rows == 4
                && schema.Multiline
                && schema.ReadOnly
                && schema.Disabled;
        }));

        checks.Add(("FieldSchema maps enum + enumNames lists", () =>
        {
            var schema = FieldSchema.FromData(Map(
                ("enum", Arr("a", "b", "c")), ("enumNames", Arr("A", "B", "C"))));
            return schema.Enum is { Count: 3 }
                && schema.EnumNames is { Count: 3 }
                && schema.EnumNames![0] == "A";
        }));

        checks.Add(("FieldSchema parses oneOf entries", () =>
        {
            var schema = FieldSchema.FromData(Map(("oneOf", Arr(
                Map(("const", 1), ("title", "One")),
                Map(("const", 2))))));
            return schema.OneOf is { Count: 2 }
                && Equals(schema.OneOf![0].Const, 1)
                && schema.OneOf[0].Title == "One"
                && schema.OneOf[1].Title == null;
        }));

        checks.Add(("FieldSchema recurses into array items", () =>
        {
            var schema = FieldSchema.FromData(Map(
                ("type", "array"), ("items", Map(("type", "string")))));
            return schema.Type == "array" && schema.Items?.Type == "string";
        }));

        checks.Add(("FieldSchema carries getOptions callback through", () =>
        {
            Func<object?> getOptions = () => Arr("x", "y");
            var schema = FieldSchema.FromData(Map(("getOptions", getOptions)));
            return schema.GetOptions != null
                && (schema.GetOptions!() as IReadOnlyList<object?>)?.Count == 2;
        }));

        checks.Add(("FieldSchema treats non-dict input as empty", () =>
        {
            var schema = FieldSchema.FromData("not-a-map");
            return schema.Type == null && schema.Title == null && !schema.Multiline;
        }));

        checks.Add(("FieldSchema passes an already-typed schema through", () =>
        {
            var original = new FieldSchema(Type: "boolean", Title: "Flag");
            return ReferenceEquals(FieldSchema.FromData(original), original);
        }));

        // ---- EditableTableSpec.FromData -----------------------------------------
        checks.Add(("EditableTableSpec maps schema/columns/flags/placeholder", () =>
        {
            var spec = EditableTableSpec.FromData(Map(
                ("schema", Map(("properties", Map(("qty", Map(("type", "integer"), ("title", "Qty"))))))),
                ("columns", Arr("qty")),
                ("addRow", false),
                ("deleteRow", true),
                ("placeholder", "None")));
            return spec.Schema?.Properties?.Count == 1
                && spec.Schema!.Properties!["qty"].Type == "integer"
                && spec.Schema.Properties["qty"].Title == "Qty"
                && spec.Columns is { Count: 1 } && spec.Columns![0] == "qty"
                && spec.AllowAddRow == false
                && spec.AllowDeleteRow
                && spec.Placeholder == "None";
        }));

        checks.Add(("EditableTableSpec defaults (empty data) keep add/delete on", () =>
        {
            var spec = EditableTableSpec.FromData(null);
            return spec.AllowAddRow && spec.AllowDeleteRow
                && spec.Placeholder == "No data"
                && (spec.Schema?.Properties?.Count ?? 0) == 0;
        }));

        // ---- FormSpec.FromData ---------------------------------------------------
        checks.Add(("FormSpec maps fields, required, saveLabel", () =>
        {
            var spec = FormSpec.FromData(Map(
                ("fields", Map(
                    ("properties", Map(("name", Map(("type", "string"))))),
                    ("required", Arr("name")))),
                ("saveLabel", "Save")));
            return spec.Fields.Properties.Count == 1
                && spec.Fields.Properties["name"].Type == "string"
                && spec.Fields.Required is { Count: 1 } && spec.Fields.Required![0] == "name"
                && spec.SaveLabel == "Save";
        }));

        checks.Add(("FormSpec parses [expr, message] array + {expr, message} object validators", () =>
        {
            var spec = FormSpec.FromData(Map(("validators", Arr(
                Arr("data.name != ''", "Name required"),
                Map(("expr", "data.age >= 18"), ("message", "18+"))))));
            return spec.Validators is { Count: 2 }
                && spec.Validators![0].Expr == "data.name != ''"
                && spec.Validators[0].Message == "Name required"
                && spec.Validators[1].Expr == "data.age >= 18"
                && spec.Validators[1].Message == "18+";
        }));

        checks.Add(("FormSpec defaults a missing validator message", () =>
        {
            var spec = FormSpec.FromData(Map(("validators", new object?[] { new object?[] { "data.x" } })));
            return spec.Validators is { Count: 1 } && spec.Validators![0].Message == "Invalid value";
        }));

        checks.Add(("FormSpec leaves validators null when none supplied", () =>
        {
            var spec = FormSpec.FromData(Map(("fields", Map(("properties", Map())))));
            return spec.Validators == null;
        }));

        // ---- ActionButton.FromData ----------------------------------------------
        checks.Add(("ActionButton maps all fields", () =>
        {
            var button = ActionButton.FromData(Map(
                ("id", "go"), ("label", "Go"), ("style", "primary"), ("size", "lg"), ("disabled", true)));
            return button is { Id: "go", Label: "Go", Style: "primary", Size: "lg", Disabled: true };
        }));

        checks.Add(("ActionButton defaults optional fields", () =>
        {
            var button = ActionButton.FromData(Map(("id", "x")));
            return button is { Id: "x", Label: null, Style: null, Size: null, Disabled: false };
        }));

        // ---- TodoItem.FromData / ToData -----------------------------------------
        checks.Add(("TodoItem maps text/done and round-trips via ToData", () =>
        {
            var item = TodoItem.FromData(Map(("text", "Buy milk"), ("done", true)));
            var data = item.ToData();
            return item is { Text: "Buy milk", Done: true }
                && Equals(data["text"], "Buy milk")
                && Equals(data["done"], true);
        }));

        checks.Add(("TodoItem defaults done to false", () =>
        {
            var item = TodoItem.FromData(Map(("text", "x")));
            return item is { Text: "x", Done: false };
        }));

        // ---- TextFile.FromData --------------------------------------------------
        checks.Add(("TextFile maps name/stored_name/size", () =>
        {
            var file = TextFile.FromData(Map(("name", "report.pdf"), ("stored_name", "abc123.pdf"), ("size", 2048)));
            return file is { Name: "report.pdf", StoredName: "abc123.pdf" } && file.Size == 2048;
        }));

        checks.Add(("TextFile defaults missing fields to null", () =>
        {
            var file = TextFile.FromData(Map(("stored_name", "only.bin")));
            return file is { Name: null, StoredName: "only.bin", Size: null };
        }));

        checks.Add(("TextFile treats non-dict input as empty", () =>
        {
            var file = TextFile.FromData("nope");
            return file is { Name: null, StoredName: null, Size: null };
        }));

        // ---- SelectOption.Normalize ---------------------------------------------
        checks.Add(("SelectOption normalizes a scalar", () =>
        {
            var option = SelectOption.Normalize("a");
            return option is { Value: "a", Label: "a" };
        }));

        checks.Add(("SelectOption normalizes value/label object", () =>
        {
            var option = SelectOption.Normalize(Map(("value", "v1"), ("label", "Label1")));
            return option is { Value: "v1", Label: "Label1" };
        }));

        checks.Add(("SelectOption falls back to id when value/label absent", () =>
        {
            var option = SelectOption.Normalize(Map(("id", "i1")));
            return option is { Value: "i1", Label: "i1" };
        }));

        // ---- registry/lib helpers (framework-agnostic, headlessly tested here) --

        // ---- RegistryPath (path.js) ---------------------------------------------
        checks.Add(("PathParts splits dotted + bracket indices", () =>
        {
            IReadOnlyList<string> parts = RegistryPath.PathParts("a.b[0].c");
            return parts.Count == 4 && parts[0] == "a" && parts[1] == "b" && parts[2] == "0" && parts[3] == "c";
        }));

        checks.Add(("PathParts is empty for null/empty", () =>
            RegistryPath.PathParts(null).Count == 0 && RegistryPath.PathParts(string.Empty).Count == 0));

        checks.Add(("DeepGet walks nested objects and array indices", () =>
        {
            var source = Map(("a", Map(("b", new List<object?> { Map(("n", 7)) }))));
            return Equals(RegistryPath.DeepGet(source, "a.b[0].n"), 7)
                && RegistryPath.DeepGet(source, "a.missing") is null;
        }));

        checks.Add(("DeepSet returns a clone without mutating the original", () =>
        {
            var source = Map(("x", 1));
            object? result = RegistryPath.DeepSet(source, "y.z", 2);
            return source.Count == 1
                && result is IReadOnlyDictionary<string, object?> map
                && Equals(map["x"], 1)
                && Equals(RegistryPath.DeepGet(map, "y.z"), 2);
        }));

        // ---- RegistryBind (bind.js) ---------------------------------------------
        checks.Add(("ResolveBind reads a namespaced path", () =>
        {
            var ns = Map(("card", Map(("meta", Map(("title", "Hi"))))));
            return (RegistryBind.ResolveBind(ns, "card.meta.title") as string) == "Hi";
        }));

        checks.Add(("ResolveBind returns the whole namespace for a single segment", () =>
        {
            var ns = Map(("boardId", "b1"));
            return (RegistryBind.ResolveBind(ns, "boardId") as string) == "b1";
        }));

        checks.Add(("ResolveBind is null for missing root / empty bind", () =>
            RegistryBind.ResolveBind(Map(("card", 1)), "nope.x") is null
            && RegistryBind.ResolveBind(Map(("card", 1)), string.Empty) is null));

        // ---- RegistryCoerce (coerce.js) -----------------------------------------
        checks.Add(("DeepEqual is structural and order-sensitive", () =>
            RegistryCoerce.DeepEqual(Map(("a", 1)), Map(("a", 1)))
            && !RegistryCoerce.DeepEqual(Map(("a", 1)), Map(("a", 2)))
            && !RegistryCoerce.DeepEqual(Map(("a", 1), ("b", 2)), Map(("b", 2), ("a", 1)))));

        checks.Add(("CoerceUnknownData passes strings, empties null, pretty-prints objects", () =>
            RegistryCoerce.CoerceUnknownData("hi") == "hi"
            && RegistryCoerce.CoerceUnknownData(null) == string.Empty
            && RegistryCoerce.CoerceUnknownData(Map(("a", 1))) == "{\n  \"a\": 1\n}"));

        checks.Add(("Stringify compact matches JSON.stringify", () =>
            RegistryCoerce.Stringify(new List<object?> { 1, "x", true }, 0) == "[1,\"x\",true]"));

        // ---- RegistryThreshold (threshold.js) -----------------------------------
        checks.Add(("ParseThreshold reads operator + value", () =>
        {
            ThresholdExpr? t = RegistryThreshold.ParseThreshold(">= 5");
            return t is not null && t.Op == ">=" && t.Value == 5
                && RegistryThreshold.ParseThreshold("nope") is null;
        }));

        checks.Add(("EvalThreshold compares per operator", () =>
            RegistryThreshold.EvalThreshold(6, ">=5")
            && !RegistryThreshold.EvalThreshold(4, ">=5")
            && RegistryThreshold.EvalThreshold(5, "==5")
            && RegistryThreshold.EvalThreshold(3, "<5")
            && !RegistryThreshold.EvalThreshold(5, "=5")));

        checks.Add(("ParseThreshold reads a leading numeric prefix like JS parseFloat", () =>
        {
            ThresholdExpr? parsed = RegistryThreshold.ParseThreshold(">= 80%");
            return parsed is not null
                && parsed.Op == ">="
                && Math.Abs(parsed.Value - 80) < 0.0001
                && RegistryThreshold.EvalThreshold(80, ">= 80%")
                && !RegistryThreshold.EvalThreshold(70, ">= 80%");
        }));

        // ---- RegistryChart (chart.js variant detection) -------------------------
        checks.Add(("DetectChartType picks pie / line / bar from the first row", () =>
        {
            IReadOnlyList<object?> pie = new List<object?> { Map(("label", "A"), ("value", 1d)) };
            IReadOnlyList<object?> line = new List<object?> { Map(("x", 1d), ("y", 2d)) };
            IReadOnlyList<object?> bar = new List<object?> { Map(("name", "A"), ("count", 2d)) };
            return RegistryChart.DetectChartType(pie) == "pie"
                && RegistryChart.DetectChartType(line) == "line"
                && RegistryChart.DetectChartType(bar) == "bar"
                && RegistryChart.DetectChartType(null) == "bar";
        }));

        checks.Add(("ResolveChartVariant honours spec.chartType, else detection", () =>
        {
            IReadOnlyList<object?> pie = new List<object?> { Map(("label", "A"), ("value", 1d)) };
            return RegistryChart.ResolveChartVariant(Map(("chartType", "line")), pie) == "line"
                && RegistryChart.ResolveChartVariant(Map(), pie) == "pie";
        }));

        // ---- RegistryFieldConfig (fieldConfig.js) -------------------------------
        checks.Add(("MergeRows clones object rows and empties non-objects", () =>
        {
            IReadOnlyDictionary<string, object?> src = Map(("k", "v"));
            IReadOnlyList<object?> data = new List<object?> { src, 5d };
            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = RegistryFieldConfig.MergeRows(data);
            return rows.Count == 2
                && !ReferenceEquals(rows[0], src)
                && rows[0]["k"] as string == "v"
                && rows[1].Count == 0
                && RegistryFieldConfig.MergeRows("nope").Count == 0;
        }));

        checks.Add(("BuildEditorSaveValue wraps card_data, else passes through", () =>
        {
            var wrapped = RegistryFieldConfig.BuildEditorSaveValue("card_data", "name", "Acme") as IReadOnlyDictionary<string, object?>;
            return wrapped != null && wrapped["name"] as string == "Acme"
                && RegistryFieldConfig.BuildEditorSaveValue("other", "name", "Acme") as string == "Acme";
        }));

        checks.Add(("GetSingleFieldConfig returns the lone field, options and required", () =>
        {
            IReadOnlyDictionary<string, object?> spec = Map(("fields", Map(
                ("properties", Map(("status", Map(
                    ("title", "Status"),
                    ("enum", new List<object?> { "open", "closed" }))))),
                ("required", new List<object?> { "status" }))));
            SingleFieldConfig? field = RegistryFieldConfig.GetSingleFieldConfig(spec, null, "open", "writeX");
            bool single = field is { FieldKey: "status", IsRequired: true }
                && field.Options.Count == 2 && field.CurrentValue as string == "open";

            IReadOnlyDictionary<string, object?> multi = Map(("fields", Map(
                ("properties", Map(("a", Map()), ("b", Map()))))));
            bool none = RegistryFieldConfig.GetSingleFieldConfig(multi, null, null, null) is null;

            object? cv = Map(("status", "closed"));
            SingleFieldConfig? field2 = RegistryFieldConfig.GetSingleFieldConfig(spec, null, cv, "card_data");
            bool unwrapped = field2?.CurrentValue as string == "closed";

            return single && none && unwrapped;
        }));

        // ---- RegistryJson (loose object model) ----------------------------------
        checks.Add(("RegistryJson maps JSON onto the loose object model", () =>
        {
            var d = RegistryJson.Parse("{\"n\":3,\"b\":true,\"s\":\"hi\",\"arr\":[1,2],\"nil\":null,\"obj\":{\"k\":\"v\"}}")
                as IReadOnlyDictionary<string, object?>;
            bool shape = d != null
                && d["n"] is double n && n == 3
                && d["b"] is true
                && d["s"] as string == "hi"
                && d["arr"] is IReadOnlyList<object?> arr && arr.Count == 2 && arr[0] is double
                && d["nil"] == null
                && (d["obj"] as IReadOnlyDictionary<string, object?>)?["k"] as string == "v";
            bool empties = RegistryJson.Parse(null) == null
                && RegistryJson.Parse("not json") == null
                && RegistryJson.ParseOrString("plain") as string == "plain";
            return shape && empties;
        }));

        var failures = 0;
        for (var i = 0; i < checks.Count; i++)
        {
            var (name, run) = checks[i];
            bool pass;
            try
            {
                pass = run();
            }
            catch (Exception ex)
            {
                pass = false;
                Console.WriteLine($"    threw: {ex.GetType().Name}: {ex.Message}");
            }

            if (!pass)
            {
                failures++;
            }

            Console.WriteLine($"[{i + 1}/{checks.Count}] {(pass ? "PASS" : "FAIL")} {name}");
        }

        if (failures == 0)
        {
            Console.WriteLine("[harness] ALL CONVERTER CHECKS PASSED");
            return 0;
        }

        Console.Error.WriteLine($"[harness] {failures} CONVERTER CHECK(S) FAILED");
        return 1;
    }

    private static Dictionary<string, object?> Map(params (string Key, object? Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value);

    private static object?[] Arr(params object?[] items) => items;
}
