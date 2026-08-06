---
drmoustache:
  source: observations
  delivery: collate
  output: summary.md
title: All observations
---

# {{count}} observations

| Station | Temperature | Recorded | Note |
|---------|-------------|----------|------|
{{#records}}| {{station}} | {{temperature}} | {{date recordedAt "yyyy-MM-dd HH:mm"}} | {{default note "—"}} |
{{/records}}
