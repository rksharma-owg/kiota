import json
import sys
from importlib.metadata import version
from pathlib import Path

sys.path.insert(0, sys.argv[1])
from models.thing import Thing
from models.thing_scalar_value import Thing_scalarValue
from models.thing_floating_value import Thing_floatingValue
from kiota_serialization_json.json_parse_node import JsonParseNode
from kiota_serialization_json.json_serialization_writer import JsonSerializationWriter

print("Python", sys.version.split()[0], "runtime", version("microsoft-kiota-serialization-json"))
cases = [
    ("scalarValue", "scalar_value", Thing_scalarValue, "boolean", False),
    ("scalarValue", "scalar_value", Thing_scalarValue, "boolean", True),
    ("scalarValue", "scalar_value", Thing_scalarValue, "integer", 0),
    ("scalarValue", "scalar_value", Thing_scalarValue, "integer", 42),
    ("scalarValue", "scalar_value", Thing_scalarValue, "thing_scalar_value_string", ""),
    ("scalarValue", "scalar_value", Thing_scalarValue, "thing_scalar_value_string", "hello"),
    ("scalarValue", "scalar_value", Thing_scalarValue, "string", []),
    ("scalarValue", "scalar_value", Thing_scalarValue, "string", ["hello"]),
    ("floatingValue", "floating_value", Thing_floatingValue, "double", 0.0),
    ("floatingValue", "floating_value", Thing_floatingValue, "double", 4.2),
]
failures = []
checks = 0
skipped = 0
for wire_name, field_name, wrapper, member_name, value in cases:
    parsed = None
    checks += 1
    try:
        parsed = JsonParseNode({wire_name: value}).get_object_value(Thing)
        observed = getattr(getattr(parsed, field_name), member_name)
        if observed != value:
            failures.append(("parse", value, observed))
    except Exception as error:
        failures.append(("parse", value, type(error).__name__, str(error)))
    instances = [("serialize", Thing(**{field_name: wrapper(**{member_name: value})}))]
    if parsed is not None:
        instances.append(("roundtrip", parsed))
    else:
        skipped += 1
    for label, instance in instances:
        writer = JsonSerializationWriter()
        writer.write_object_value(None, instance)
        observed = json.loads(writer.get_serialized_content())[wire_name]
        checks += 1
        if observed != value:
            failures.append((label, value, observed))
# Preserve existing behavior for a wrapper with no populated member.
writer = JsonSerializationWriter()
writer.write_object_value(None, Thing(scalar_value=Thing_scalarValue()))
assert json.loads(writer.get_serialized_content())["scalarValue"] == {}
checks += 1
# A model property left unset must remain unset after parsing.
assert JsonParseNode({}).get_object_value(Thing).scalar_value is None
checks += 1
print("Checks:", checks, "Failures:", len(failures), "Skipped roundtrips after parse exception:", skipped)
for failure in failures:
    print(failure)
if failures:
    raise SystemExit(1)
