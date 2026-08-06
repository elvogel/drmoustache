---
drmoustache:
  source: main
  delivery: fanout
  output: reports/{{id}}.md
  onCollision: suffix
title: "{{name}}"
---

# {{name}}

Value: {{value}}
