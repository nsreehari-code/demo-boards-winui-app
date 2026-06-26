using System;
using System.Collections.Generic;
using System.Linq;
using DemoBoards_WinUI.Controls.Shared;

namespace ConverterHarness;

// Parity harness for the plain-data -> typed-record converters that back the
// shared board components. Each component now takes plain data props and calls
// one of these FromData converters internally; this asserts the conversions
// preserve every data-driven field the frontend authors.
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
