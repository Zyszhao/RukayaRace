# 开发版在模组列表中正常显示简介和依赖

- 重启 RimWorld，在模组列表中按目录确认选中 `Rukaya Race Dev Branch`，本次只启用开发版。
- 查看简介、封面、支持版本和前置依赖；启用 Harmony、HAR、Biotech 后进入主菜单并查看日志。

预期：简介显示现有中文内容，封面显示开发目录的 `About/Preview.png`；支持版本为 1.7，前置依赖完整；没有该目录 About.xml 的 XML 解析或元数据读取错误。开发版 packageId 保持 `zyszhao.rukayarace.dev`。
