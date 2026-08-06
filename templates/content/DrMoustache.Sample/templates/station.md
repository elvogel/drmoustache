---
drmoustache:
  source: observations
  delivery: fanout
  output: stations/{{station}}.md
  onCollision: suffix
title: "{{station}}"
layout: observation
---

# Station {{station}}

Recorded {{date recordedAt "yyyy-MM-dd HH:mm"}} at {{temperature}}°C.

{{#note}}> {{.}}
{{/note}}
