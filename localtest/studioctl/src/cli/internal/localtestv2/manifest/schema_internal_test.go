package manifest

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"math"
	"reflect"
	"regexp"
	"slices"
	"strings"
	"testing"
)

// The manifest is validated against its JSON Schema with the small validator below, which knows exactly the keywords
// the schema uses and refuses any other, so a keyword added to the schema cannot be skipped silently. The module takes
// no JSON Schema dependency for one test; a full 2020-12 validator (see README.md) is the manual check.

func TestTheManifestValidatesAgainstItsSchema(t *testing.T) {
	t.Parallel()
	v := newSchemaValidator(t)
	if errs := v.validate(v.root, decodeJSON(t, manifestJSON), "$"); len(errs) > 0 {
		t.Fatalf("manifest.json does not match manifest.schema.json:\n%s", strings.Join(errs, "\n"))
	}
}

func TestTheSchemaRefusesWhatTheManifestMustNotHold(t *testing.T) {
	t.Parallel()
	v := newSchemaValidator(t)
	cases := map[string]func(m map[string]any){
		"unknown top-level key": func(m map[string]any) { m["extra"] = true },
		"missing components":    func(m map[string]any) { delete(m, "components") },
		"other version":         func(m map[string]any) { m["version"] = 2.0 },
		"string port":           func(m map[string]any) { firstComponent(m)["containerPort"] = "5432" },
		"unknown component key": func(m map[string]any) { firstComponent(m)["ports"] = []any{} },
		"bad component name":    func(m map[string]any) { firstComponent(m)["name"] = "Postgres DB" },
		"mount with a target on the certificate directory": func(m map[string]any) {
			firstComponent(m)["mounts"] = []any{map[string]any{"kind": "certificateDir", "target": "/x"}}
		},
		"unknown mount kind": func(m map[string]any) {
			firstComponent(m)["mounts"] = []any{map[string]any{"kind": "tmpfs", "target": "/x"}}
		},
	}
	for name, mutate := range cases {
		m := asObject(decodeJSON(t, manifestJSON))
		mutate(m)
		if errs := v.validate(v.root, m, "$"); len(errs) == 0 {
			t.Errorf("%s: the schema accepts it", name)
		}
	}
}

// TestEverySchemaPropertyIsDocumented keeps editors helpful: every property, definition and alternative has a title
// and a description.
func TestEverySchemaPropertyIsDocumented(t *testing.T) {
	t.Parallel()
	var missing []string
	var walk func(node any, path string)
	walk = func(node any, path string) {
		obj, isObject := node.(map[string]any)
		if !isObject {
			return
		}
		if _, isRef := obj["$ref"]; !isRef {
			for _, key := range []string{"title", "description"} {
				if text, isString := obj[key].(string); !isString || strings.TrimSpace(text) == "" {
					missing = append(missing, path+": "+key)
				}
			}
		}
		for _, key := range []string{"properties", "$defs"} {
			if children, ok := obj[key].(map[string]any); ok {
				for name, child := range children {
					walk(child, path+"/"+key+"/"+name)
				}
			}
		}
		for _, key := range []string{"items", "additionalProperties"} {
			walk(obj[key], path+"/"+key)
		}
		if alternatives, ok := obj["oneOf"].([]any); ok {
			for i, alt := range alternatives {
				walk(alt, fmt.Sprintf("%s/oneOf/%d", path, i))
			}
		}
	}
	walk(decodeJSON(t, schemaJSON), "#")
	if len(missing) > 0 {
		slices.Sort(missing)
		t.Errorf("undocumented schema nodes:\n%s", strings.Join(missing, "\n"))
	}
}

func asObject(v any) map[string]any {
	obj, ok := v.(map[string]any)
	if !ok {
		panic(fmt.Sprintf("not a JSON object: %T", v))
	}
	return obj
}

func asList(v any) []any {
	list, ok := v.([]any)
	if !ok {
		panic(fmt.Sprintf("not a JSON array: %T", v))
	}
	return list
}

func firstComponent(m map[string]any) map[string]any {
	return asObject(asList(m["components"])[0])
}

func decodeJSON(t *testing.T, data []byte) any {
	t.Helper()
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.UseNumber()
	var v any
	if err := decoder.Decode(&v); err != nil {
		t.Fatal(err)
	}
	return normalizeNumbers(v)
}

// normalizeNumbers turns json.Number into float64, so const and enum compare numbers by value.
func normalizeNumbers(v any) any {
	switch x := v.(type) {
	case json.Number:
		f, err := x.Float64()
		if err != nil {
			panic(err)
		}
		return f
	case map[string]any:
		for k, child := range x {
			x[k] = normalizeNumbers(child)
		}
	case []any:
		for i, child := range x {
			x[i] = normalizeNumbers(child)
		}
	}
	return v
}

var errSchemaKeyword = errors.New("schema keyword the test validator does not know")

// keywordCheck checks one keyword of a schema node against a value and returns the violations.
type keywordCheck func(v *schemaValidator, node map[string]any, arg, value any, path string) []string

// schemaValidator checks a value against a JSON Schema 2020-12 node that uses only the keywords in checks.
type schemaValidator struct {
	root   any
	checks map[string]keywordCheck
}

func newSchemaValidator(t *testing.T) *schemaValidator {
	t.Helper()
	annotation := func(*schemaValidator, map[string]any, any, any, string) []string { return nil }
	return &schemaValidator{
		root: decodeJSON(t, schemaJSON),
		checks: map[string]keywordCheck{
			"$schema":              annotation,
			"$id":                  annotation,
			"title":                annotation,
			"description":          annotation,
			"$defs":                annotation,
			"$ref":                 checkRef,
			"type":                 checkType,
			"const":                checkConst,
			"enum":                 checkEnum,
			"minLength":            checkMinLength,
			"pattern":              checkPattern,
			"minimum":              checkMinimum,
			"maximum":              checkMaximum,
			"required":             checkRequired,
			"properties":           checkProperties,
			"additionalProperties": checkProperties,
			"propertyNames":        checkPropertyNames,
			"minProperties":        checkPropertyCount,
			"maxProperties":        checkPropertyCount,
			"items":                checkItems,
			"minItems":             checkMinItems,
			"uniqueItems":          checkUniqueItems,
			"oneOf":                checkOneOf,
		},
	}
}

// validate returns every violation of schema by value, with its JSON path.
func (v *schemaValidator) validate(schema, value any, path string) []string {
	node, ok := schema.(map[string]any)
	if !ok {
		return []string{path + ": schema node is not an object"}
	}
	var errs []string
	for keyword, arg := range node {
		check, known := v.checks[keyword]
		if !known {
			errs = append(errs, fmt.Sprintf("%s: %v %q", path, errSchemaKeyword, keyword))
			continue
		}
		errs = append(errs, check(v, node, arg, value, path)...)
	}
	return errs
}

func violation(path, format string, args ...any) []string {
	return []string{path + ": " + fmt.Sprintf(format, args...)}
}

func checkRef(v *schemaValidator, _ map[string]any, arg, value any, path string) []string {
	ref := fmt.Sprint(arg)
	name, ok := strings.CutPrefix(ref, "#/$defs/")
	if !ok {
		return violation(path, "%v: $ref %q (only #/$defs/<name>)", errSchemaKeyword, ref)
	}
	target, ok := asObject(asObject(v.root)["$defs"])[name]
	if !ok {
		return violation(path, "%v: unresolved $ref %q", errSchemaKeyword, ref)
	}
	return v.validate(target, value, path)
}

func checkType(_ *schemaValidator, _ map[string]any, arg, value any, path string) []string {
	if jsonType(value) == arg || (arg == "integer" && jsonType(value) == "number" && isInteger(value)) {
		return nil
	}
	return violation(path, "type %v, got %s", arg, jsonType(value))
}

func jsonType(value any) string {
	switch value.(type) {
	case map[string]any:
		return "object"
	case []any:
		return "array"
	case string:
		return "string"
	case bool:
		return "boolean"
	case float64:
		return "number"
	case nil:
		return "null"
	default:
		return fmt.Sprintf("%T", value)
	}
}

func isInteger(value any) bool {
	n, ok := value.(float64)
	return ok && n == math.Trunc(n)
}

func checkConst(_ *schemaValidator, _ map[string]any, arg, value any, path string) []string {
	if reflect.DeepEqual(value, arg) {
		return nil
	}
	return violation(path, "must be %v", arg)
}

func checkEnum(_ *schemaValidator, _ map[string]any, arg, value any, path string) []string {
	if slices.ContainsFunc(asList(arg), func(o any) bool { return reflect.DeepEqual(o, value) }) {
		return nil
	}
	return violation(path, "must be one of %v", arg)
}

func checkMinLength(_ *schemaValidator, _ map[string]any, arg, value any, path string) []string {
	if s, isString := value.(string); isString && float64(len([]rune(s))) < number(arg) {
		return violation(path, "shorter than %v", arg)
	}
	return nil
}

func checkPattern(_ *schemaValidator, _ map[string]any, arg, value any, path string) []string {
	pattern := fmt.Sprint(arg)
	if s, isString := value.(string); isString && !regexp.MustCompile(pattern).MatchString(s) {
		return violation(path, "%q does not match %s", s, pattern)
	}
	return nil
}

func checkMinimum(_ *schemaValidator, _ map[string]any, arg, value any, path string) []string {
	if n, isNumber := value.(float64); isNumber && n < number(arg) {
		return violation(path, "below %v", arg)
	}
	return nil
}

func checkMaximum(_ *schemaValidator, _ map[string]any, arg, value any, path string) []string {
	if n, isNumber := value.(float64); isNumber && n > number(arg) {
		return violation(path, "above %v", arg)
	}
	return nil
}

func checkRequired(_ *schemaValidator, _ map[string]any, arg, value any, path string) []string {
	obj, isObject := value.(map[string]any)
	if !isObject {
		return nil
	}
	var errs []string
	for _, name := range asList(arg) {
		if _, present := obj[fmt.Sprint(name)]; !present {
			errs = append(errs, violation(path, "missing %v", name)...)
		}
	}
	return errs
}

// checkProperties applies properties and additionalProperties together, once per node: when both are present, the
// additionalProperties keyword defers to properties.
func checkProperties(v *schemaValidator, node map[string]any, arg, value any, path string) []string {
	if _, both := node["properties"]; both && !reflect.DeepEqual(node["properties"], arg) {
		return nil
	}
	obj, isObject := value.(map[string]any)
	if !isObject {
		return nil
	}
	properties := map[string]any{}
	if declared, ok := node["properties"].(map[string]any); ok {
		properties = declared
	}
	additional := node["additionalProperties"]
	var errs []string
	for name, child := range obj {
		if propertySchema, known := properties[name]; known {
			errs = append(errs, v.validate(propertySchema, child, path+"."+name)...)
			continue
		}
		switch additional {
		case false:
			errs = append(errs, violation(path+"."+name, "not allowed")...)
		case true, nil:
		default:
			errs = append(errs, v.validate(additional, child, path+"."+name)...)
		}
	}
	return errs
}

func checkPropertyNames(v *schemaValidator, _ map[string]any, arg, value any, path string) []string {
	obj, isObject := value.(map[string]any)
	if !isObject {
		return nil
	}
	var errs []string
	for name := range obj {
		errs = append(errs, v.validate(arg, name, path+" key "+name)...)
	}
	return errs
}

func checkPropertyCount(_ *schemaValidator, node map[string]any, _, value any, path string) []string {
	obj, isObject := value.(map[string]any)
	if !isObject {
		return nil
	}
	n := float64(len(obj))
	if limit, ok := node["minProperties"]; ok && n < number(limit) {
		return violation(path, "fewer than %v properties", limit)
	}
	if limit, ok := node["maxProperties"]; ok && n > number(limit) {
		return violation(path, "more than %v properties", limit)
	}
	return nil
}

func checkItems(v *schemaValidator, _ map[string]any, arg, value any, path string) []string {
	items, isArray := value.([]any)
	if !isArray {
		return nil
	}
	var errs []string
	for i, item := range items {
		errs = append(errs, v.validate(arg, item, fmt.Sprintf("%s[%d]", path, i))...)
	}
	return errs
}

func checkMinItems(_ *schemaValidator, _ map[string]any, arg, value any, path string) []string {
	if items, isArray := value.([]any); isArray && float64(len(items)) < number(arg) {
		return violation(path, "fewer than %v items", arg)
	}
	return nil
}

func checkUniqueItems(_ *schemaValidator, _ map[string]any, arg, value any, path string) []string {
	items, isArray := value.([]any)
	if !isArray || arg != true {
		return nil
	}
	for i := range items {
		for j := range i {
			if reflect.DeepEqual(items[i], items[j]) {
				return violation(path, "items %d and %d are equal", j, i)
			}
		}
	}
	return nil
}

func checkOneOf(v *schemaValidator, _ map[string]any, arg, value any, path string) []string {
	matched := 0
	for _, alternative := range asList(arg) {
		if len(v.validate(alternative, value, path)) == 0 {
			matched++
		}
	}
	if matched != 1 {
		return violation(path, "matches %d of the oneOf alternatives, want exactly 1", matched)
	}
	return nil
}

func number(v any) float64 {
	n, ok := v.(float64)
	if !ok {
		panic(fmt.Sprintf("not a JSON number: %T", v))
	}
	return n
}
