(module
  (func (export "bgb_api") (result i32) i32.const 1)
  (func (export "bgb_version") (result i32) i32.const 1)
  (func (export "bgb_init") (result i32) i32.const 3)
  (func (export "bgb_command") (param $current i32) (param $delta i32) (result i32)
    local.get $current local.get $delta i32.add))
