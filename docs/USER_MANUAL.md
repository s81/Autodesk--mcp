<!-- Copy of the shared Autodesk MCP User Manual doc (English and Arabic tabs). Keep the two in step. -->

**Language / اللغة:** [English](#english) · [العربية](#arabic)

<a id="english"></a>

# Autodesk MCP User Manual

Last updated: 2026-11-06 (version 0.3.1)

Autodesk MCP lets Claude read and edit the Revit or AutoCAD model you have open, on your own Windows PC, from plain-language requests. This manual covers version 0.3.1: Part 1 is for everyday users, Part 2 for IT staff who install it. An Arabic version follows [below](#arabic).

## Part 1 — For everyday users

### What it does

Claude works on the file open in Revit or AutoCAD 2022–2027 on the same PC. Nothing goes over the network to Revit or AutoCAD, and Claude can only do what the tools below allow.

| Area | Revit | AutoCAD |
| --- | --- | --- |
| Read | Project info, levels, views, elements and their parameters, the current selection, schedule data | Drawing info, layers, entities and their properties, blocks |
| Change | Set parameters, create walls, delete elements, change the selection | Create layers, draw lines, polylines, arcs, circles and text, move or modify entities, insert blocks, run commands |
| Advanced | Run a short C# script for anything else | Run a short C# script for anything else |

It also works in AutoCAD-based products such as Civil 3D and Advance Steel, but not in AutoCAD LT.

### Before you start

IT installs it once per PC (Part 2). Then, each time:

1. Start Revit or AutoCAD **normally** from the Start menu or desktop, not with "Run as administrator". Claude and Revit/AutoCAD must run the same way, or they cannot connect.
2. Open the project or drawing you want to work on.
3. Open Claude Desktop.
4. Optional check: ask Claude *"List my running Autodesk instances."* Your program should appear with `reachable: true`.

You can have Revit and AutoCAD open at the same time. If two copies of the same program are open, Claude asks which one you mean.

### Asking Claude

Write what you want in plain words, in English or Arabic. Name the program when both are open, and give units, levels, layers or element IDs when they matter.

| Task | Example request |
| --- | --- |
| Count and list | "How many doors are on Level 2? List them with their type and mark." |
| Check data | "Which walls have an empty Fire Rating parameter?" |
| Fill parameters | "Set the Comments of the selected elements to 'Phase 2'." |
| Schedules | "Show the data of the Door Schedule." |
| Model | "Create a wall on Level 1 from 0,0 to 6000,0 mm, 3000 mm high." |
| AutoCAD layers | "Create a layer A-ANNO-DIMS in yellow." |
| AutoCAD drawing | "On layer S-STEEL draw a rectangle from 0,0 to 300,150." |
| AutoCAD edits | "Move everything on layer TEMP by 500,0." |
| Anything else | "Using C#, list every family type in the project with its instance count." |

Tips:

- Ask Claude to **show you first** before a big change: *"List the walls you would change, but do not change them yet."*
- For C# requests, add *"without committing"* when you only want to read data.
- If Claude picks the wrong element, give the element ID (Revit) or handle (AutoCAD).

### Safety and undo

- **Allow prompts:** Claude asks before using a tool. Read what it is about to do, then click **Allow** or **Deny**. Look closely at delete requests and C# scripts.
- **Undo:** each change Claude makes is one step in the program's history. Press **Ctrl+Z** in Revit or AutoCAD to reverse it.
- **Work on copies** while you learn the tool, and save before large changes.
- **Nothing runs while you are busy:** if a dialog is open (Revit) or a command is running (AutoCAD), Claude waits or reports that the program is busy. Requests waiting more than 2 minutes are cancelled and never run.
- **C# scripts** run inside Revit or AutoCAD with your Windows rights. Only allow scripts you understand or that only read data. IT can turn scripts off (Part 2).
- **Only you:** the connection accepts your own Windows account on this PC, and nothing listens on the network.

### Troubleshooting

| What you see | Likely cause | What to do |
| --- | --- | --- |
| Claude has no Autodesk tools | Claude was open during install | Quit Claude fully (tray icon → Quit) and reopen it |
| "No running revit/autocad instance with the Autodesk MCP add-in was found" | Add-in did not load, or the program was started before install | Close and restart Revit/AutoCAD; choose **Always Load** if asked; then tell IT |
| Instance listed but `reachable: false`, or "not owned by the current user" | One program runs as administrator and the other does not | Start both normally, not "Run as administrator" |
| "busy" or a timeout | A dialog is open or a command is running | Close the dialog or press **Esc**, then ask again |
| "… modifications are disabled" | IT turned off changes on this PC | Read-only use only; ask IT if you need changes |
| "Code execution is disabled" | IT turned off C# scripts | Ask for the result without C#, or ask IT |
| Wrong element changed | The request was ambiguous | Press **Ctrl+Z**, then repeat with the element ID or handle |
| addinSetup lists "problems" (extension only) | Revit/AutoCAD was open, so its add-in file was in use | Close Revit/AutoCAD, quit and reopen Claude, then start Revit/AutoCAD |

Still stuck? Send IT the time it happened, Claude's exact message, and the newest log from `%LOCALAPPDATA%\AutodeskMcp\logs`.

## Part 2 — For IT staff

### Requirements and supported versions

- Windows 10 or 11, 64-bit. No administrator rights and no separate .NET install are needed; everything installs per user.
- Claude Desktop or Claude Code on the same PC, signed in.
- The Revit and AutoCAD years below. 2022–2024 are supported but not yet tested inside the real programs.

| Year | Revit add-in runtime | AutoCAD release | AutoCAD add-in runtime |
| --- | --- | --- | --- |
| 2027 | .NET 10 | R26.0 | .NET 10 |
| 2026 | .NET 8 | R25.1 | .NET 8 |
| 2025 | .NET 8 | R25.0 | .NET 8 |
| 2024 | .NET Framework 4.8 | R24.3 | .NET Framework 4.8 |
| 2023 | .NET Framework 4.8 | R24.2 | .NET Framework 4.8 |
| 2022 | .NET Framework 4.8 | R24.1 | .NET Framework 4.8 |

Download from the [latest release](https://github.com/s81/Autodesk--mcp/releases/latest): **AutodeskMcp-v0.3.1.mcpb** (Claude Desktop extension, easiest) or **AutodeskMcp-All-v0.3.1.zip** (installer script). Checksums are in `SHA256SUMS.txt`.

### Install, connect Claude, update and remove

**Easiest: the Claude Desktop extension (.mcpb).** It installs the MCP server and the add-ins in one step, with no zip, PowerShell or config editing.

1. Close Revit and AutoCAD.
2. Download **AutodeskMcp-v0.3.1.mcpb**.
3. In Claude Desktop open **Settings → Desktop app → Manage** (next to Extensions; **Settings → Extensions** in older versions) → **Advanced settings** → **Install Extension…**, choose the file and follow the prompts. The extension is not code-signed yet, so Claude may warn about that.
4. In the extension's settings choose whether to allow changes and C# scripts, and the busy timeout. These are written to `config.json` (below).
5. Start Revit/AutoCAD. Each time Claude starts the extension, it copies the add-ins for every year installed under `C:\Program Files\Autodesk`, copying only changed files. Ask Claude *"List my running Autodesk instances."*: `addinSetup` shows what was installed and any `problems` (for example, a DLL in use because Revit was open: close it and restart Claude).

To update, install the new .mcpb the same way. Removing the extension in Claude does not remove the add-ins; run `install.ps1 -Uninstall` from the zip for that. Use only one method per PC: the extension or the installer script below.

**Alternative: zip and installer script.**

Install, signed in as the person who will use it:

1. Right-click the zip → **Properties** → tick **Unblock** if shown → **OK**. Do this before extracting; the 2022–2024 programs can refuse blocked DLLs.
2. Extract it, for example to `C:\AutodeskMcp`.
3. Close Revit and AutoCAD.
4. In the extracted folder's address bar, type `powershell` and press Enter, then run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
```

With no options it installs for every Revit and AutoCAD year found under `C:\Program Files\Autodesk`. To choose, add one year per run, such as `-RevitYears 2024` or `-AutoCADYears 2026`, or skip a product with `-SkipRevit` or `-SkipAutoCAD`.

Connect Claude Desktop: open **Settings → Developer → Edit Config** and add this to `claude_desktop_config.json` (replace `<user>` with the Windows user name; the installer prints the exact path):

```json
{
  "mcpServers": {
    "autodesk": { "command": "C:\\Users\\<user>\\AppData\\Local\\AutodeskMcp\\server\\AutodeskMcp.Server.exe" }
  }
}
```

Then quit Claude fully (tray icon → Quit) and reopen it. For Claude Code, run the `claude mcp add autodesk -- "…\AutodeskMcp.Server.exe"` line the installer prints.

Update: download the new zip, unblock and extract it, close Revit, AutoCAD and Claude, and run the same install command. The Claude config does not change.

Remove:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Uninstall
```

Then delete the `autodesk` entry from the Claude config. Settings and logs stay in `%LOCALAPPDATA%\AutodeskMcp` until you delete that folder.

### Settings, security and where files live

Policy lives in `%LOCALAPPDATA%\AutodeskMcp\config.json`. Create it if it is missing; Revit and AutoCAD read it when they start, so restart them after a change. The add-in enforces it, so Claude cannot bypass it.

With the Claude Desktop extension, change `allowModifications`, `allowCodeExecution` and `mainThreadTimeoutSeconds` in the extension's settings instead: it writes them to this file each time Claude starts, so hand edits to those three keys are replaced.

```json
{
  "enabled": true,
  "allowModifications": true,
  "allowCodeExecution": true,
  "mainThreadTimeoutSeconds": 120
}
```

| Setting | Default | Effect when changed |
| --- | --- | --- |
| `enabled` | true | false: the add-in does not accept connections at all |
| `allowModifications` | true | false: read-only; every tool that changes a model or drawing is refused |
| `allowCodeExecution` | true | false: C# scripts are refused |
| `mainThreadTimeoutSeconds` | 120 | How long a request waits for a busy program before it is cancelled (it never runs late) |

For most users, a safe start is `allowCodeExecution: false`; turn it on for people who need scripts.

Security:

- The connection is a local named pipe that accepts only the current Windows user, and each request carries a random 256-bit token stored in the user's profile. Nothing listens on the network.
- Claude and Revit/AutoCAD must run at the same permission level; a program started as administrator cannot be reached from a normal Claude, and the other way round.
- C# scripts run with the user's Windows rights inside Revit or AutoCAD.

| What | Where |
| --- | --- |
| MCP server | `%LOCALAPPDATA%\AutodeskMcp\server\AutodeskMcp.Server.exe` |
| Revit add-in | `%APPDATA%\Autodesk\Revit\Addins\<year>\AutodeskMcp.addin` and `AutodeskMcp\` |
| AutoCAD bundle | `%APPDATA%\Autodesk\ApplicationPlugins\AutodeskMcp.bundle` (one bundle for all years) |
| Policy | `%LOCALAPPDATA%\AutodeskMcp\config.json` |
| Running programs | `%LOCALAPPDATA%\AutodeskMcp\instances\` (one file per open Revit/AutoCAD) |
| Logs | `%LOCALAPPDATA%\AutodeskMcp\logs\` |

Source code, issues and releases: [github.com/s81/Autodesk--mcp](https://github.com/s81/Autodesk--mcp).

---

<a id="arabic"></a>

<div dir="rtl" lang="ar">

# دليل استخدام Autodesk MCP

يتيح Autodesk MCP لـ Claude أن يقرأ ويعدّل نموذج Revit أو رسم AutoCAD المفتوح على جهازك، بطلبات بلغة عادية. يغطي هذا الدليل الإصدار 0.3.1: الجزء الأول للمستخدمين يوميًا، والجزء الثاني لفريق تقنية المعلومات الذي يقوم بالتثبيت. أسماء القوائم والأوامر مكتوبة بالإنجليزية كما تظهر في البرامج.

## الجزء الأول — للمستخدمين

### ماذا يفعل

يعمل Claude على الملف المفتوح في Revit أو AutoCAD من إصدار 2022 إلى 2027 على الجهاز نفسه. لا يُرسَل أي شيء عبر الشبكة إلى Revit أو AutoCAD، ولا يستطيع Claude أن يفعل إلا ما تسمح به الأدوات التالية.

| المجال | Revit | AutoCAD |
| --- | --- | --- |
| القراءة | بيانات المشروع، المستويات (Levels)، المناظر (Views)، العناصر ومعاملاتها (Parameters)، التحديد الحالي، بيانات الجداول (Schedules) | بيانات الرسم، الطبقات (Layers)، الكائنات وخصائصها، البلوكات (Blocks) |
| التعديل | تعبئة المعاملات، إنشاء الجدران، حذف العناصر، تغيير التحديد | إنشاء الطبقات، رسم الخطوط والخطوط المتعددة والأقواس والدوائر والنصوص، نقل الكائنات أو تعديلها، إدراج البلوكات، تنفيذ الأوامر |
| متقدم | تشغيل سكربت C# قصير لأي مهمة أخرى | تشغيل سكربت C# قصير لأي مهمة أخرى |

يعمل أيضًا في البرامج المبنية على AutoCAD مثل Civil 3D وAdvance Steel، لكنه لا يعمل في AutoCAD LT.

### قبل أن تبدأ

يقوم فريق تقنية المعلومات بالتثبيت مرة واحدة لكل جهاز (الجزء الثاني). بعد ذلك، في كل مرة:

1. شغّل Revit أو AutoCAD **بشكل عادي** من قائمة ابدأ أو سطح المكتب، وليس باختيار "Run as administrator". يجب أن يعمل Claude وRevit/AutoCAD بالطريقة نفسها، وإلا لن يتمكنا من الاتصال.
2. افتح المشروع أو الرسم الذي تريد العمل عليه.
3. افتح Claude Desktop.
4. تحقق اختياري: اطلب من Claude *"List my running Autodesk instances."* يجب أن يظهر برنامجك مع `reachable: true`.

يمكنك فتح Revit وAutoCAD في الوقت نفسه. إذا كانت هناك نسختان مفتوحتان من البرنامج نفسه، سيسألك Claude أيهما تقصد.

### كيف تطلب من Claude

اكتب ما تريده بكلمات عادية، بالعربية أو الإنجليزية. اذكر اسم البرنامج إذا كان الاثنان مفتوحين، وحدّد الوحدات أو المستويات أو الطبقات أو أرقام العناصر عند الحاجة.

| المهمة | مثال على الطلب |
| --- | --- |
| العدّ والسرد | "كم بابًا في Level 2؟ اسردها مع النوع والـ Mark." |
| فحص البيانات | "ما الجدران التي معامل Fire Rating فيها فارغ؟" |
| تعبئة المعاملات | "اكتب 'Phase 2' في Comments للعناصر المحددة." |
| الجداول | "اعرض بيانات Door Schedule." |
| النمذجة | "أنشئ جدارًا في Level 1 من 0,0 إلى 6000,0 ملم بارتفاع 3000 ملم." |
| طبقات AutoCAD | "أنشئ طبقة A-ANNO-DIMS باللون الأصفر." |
| الرسم في AutoCAD | "على الطبقة S-STEEL ارسم مستطيلًا من 0,0 إلى 300,150." |
| التعديل في AutoCAD | "انقل كل ما على الطبقة TEMP بمقدار 500,0." |
| أي شيء آخر | "باستخدام C#، اسرد كل أنواع العائلات (Family Types) في المشروع مع عدد النسخ من كل نوع." |

نصائح:

- اطلب من Claude أن **يعرض أولًا** قبل أي تغيير كبير: *"اسرد الجدران التي ستغيّرها، لكن لا تغيّرها الآن."*
- في طلبات C#، أضف *"بدون حفظ التغييرات"* (without committing) عندما تريد قراءة البيانات فقط.
- إذا اختار Claude عنصرًا خاطئًا، أعطه رقم العنصر (Element ID في Revit) أو الـ Handle (في AutoCAD).

### الأمان والتراجع

- **طلبات السماح:** يستأذنك Claude قبل استخدام أي أداة. اقرأ ما سيفعله، ثم اضغط **Allow** للسماح أو **Deny** للرفض. دقّق جيدًا في طلبات الحذف وسكربتات C#.
- **التراجع:** كل تغيير يقوم به Claude هو خطوة واحدة في سجل البرنامج. اضغط **Ctrl+Z** في Revit أو AutoCAD للتراجع عنه.
- **اعمل على نسخ** أثناء تعلّم الأداة، واحفظ الملف قبل التغييرات الكبيرة.
- **لا يُنفَّذ شيء وأنت مشغول:** إذا كانت نافذة حوار مفتوحة (Revit) أو أمر قيد التنفيذ (AutoCAD)، ينتظر Claude أو يُبلغك بأن البرنامج مشغول. الطلبات التي تنتظر أكثر من دقيقتين تُلغى ولا تُنفَّذ أبدًا.
- **سكربتات C#** تعمل داخل Revit أو AutoCAD بصلاحيات حسابك في Windows. لا تسمح إلا بالسكربتات التي تفهمها أو التي تقرأ البيانات فقط. يستطيع فريق تقنية المعلومات إيقافها (الجزء الثاني).
- **أنت فقط:** الاتصال يقبل حسابك في Windows على هذا الجهاز فقط، ولا يستمع أي شيء على الشبكة.

### حل المشكلات

| ما تراه | السبب المحتمل | ماذا تفعل |
| --- | --- | --- |
| لا توجد أدوات Autodesk في Claude | كان Claude مفتوحًا أثناء التثبيت | أغلق Claude بالكامل (أيقونة شريط المهام ← Quit) ثم افتحه من جديد |
| "No running revit/autocad instance with the Autodesk MCP add-in was found" | لم تُحمَّل الإضافة، أو شُغِّل البرنامج قبل التثبيت | أغلق Revit/AutoCAD وأعد تشغيله، واختر **Always Load** إذا ظهر السؤال، ثم أبلغ فريق تقنية المعلومات |
| البرنامج ظاهر لكن `reachable: false`، أو رسالة "not owned by the current user" | أحد البرنامجين يعمل كمسؤول (administrator) والآخر لا | شغّل الاثنين بشكل عادي، وليس بـ "Run as administrator" |
| "busy" أو انتهاء المهلة | نافذة حوار مفتوحة أو أمر قيد التنفيذ | أغلق النافذة أو اضغط **Esc**، ثم اطلب من جديد |
| "… modifications are disabled" | أوقف فريق تقنية المعلومات التعديلات على هذا الجهاز | الاستخدام للقراءة فقط؛ اطلب من فريق تقنية المعلومات إذا احتجت للتعديل |
| "Code execution is disabled" | أوقف فريق تقنية المعلومات سكربتات C# | اطلب النتيجة بدون C#، أو راجع فريق تقنية المعلومات |
| تغيّر عنصر خاطئ | الطلب كان غير واضح | اضغط **Ctrl+Z**، ثم أعد الطلب مع رقم العنصر أو الـ Handle |
| ظهور "problems" في addinSetup (الإضافة فقط) | كان Revit/AutoCAD مفتوحًا، فكان ملف الإضافة مستخدمًا | أغلق Revit/AutoCAD، وأغلق Claude وافتحه من جديد، ثم شغّل Revit/AutoCAD |

ما زالت المشكلة قائمة؟ أرسل إلى فريق تقنية المعلومات وقت حدوثها، ورسالة Claude كما هي، وأحدث ملف سجل من `%LOCALAPPDATA%\AutodeskMcp\logs`.

## الجزء الثاني — لفريق تقنية المعلومات

### المتطلبات والإصدارات المدعومة

- Windows 10 أو 11 بنظام 64-bit. لا تحتاج إلى صلاحيات مسؤول ولا إلى تثبيت .NET منفصل؛ كل شيء يُثبَّت لكل مستخدم على حدة.
- Claude Desktop أو Claude Code على الجهاز نفسه، مع تسجيل الدخول.
- إصدارات Revit وAutoCAD التالية. الإصدارات 2022–2024 مدعومة لكنها لم تُختبر بعد داخل البرامج الفعلية.

| السنة | بيئة تشغيل إضافة Revit | إصدار AutoCAD | بيئة تشغيل إضافة AutoCAD |
| --- | --- | --- | --- |
| 2027 | .NET 10 | R26.0 | .NET 10 |
| 2026 | .NET 8 | R25.1 | .NET 8 |
| 2025 | .NET 8 | R25.0 | .NET 8 |
| 2024 | .NET Framework 4.8 | R24.3 | .NET Framework 4.8 |
| 2023 | .NET Framework 4.8 | R24.2 | .NET Framework 4.8 |
| 2022 | .NET Framework 4.8 | R24.1 | .NET Framework 4.8 |

التنزيل من [أحدث إصدار](https://github.com/s81/Autodesk--mcp/releases/latest): **AutodeskMcp-v0.3.1.mcpb** (إضافة Claude Desktop، الأسهل) أو **AutodeskMcp-All-v0.3.1.zip** (سكربت التثبيت). الـ Checksums موجودة في `SHA256SUMS.txt`.

### التثبيت والربط بـ Claude والتحديث والإزالة

**الأسهل: إضافة Claude Desktop بصيغة .mcpb.** تثبّت خادم MCP والإضافات في خطوة واحدة، دون zip أو PowerShell أو تعديل ملف الإعداد.

1. أغلق Revit وAutoCAD.
2. نزّل **AutodeskMcp-v0.3.1.mcpb**.
3. في Claude Desktop افتح **Settings ← Desktop app ← Manage** (بجانب Extensions؛ أو **Settings ← Extensions** في الإصدارات الأقدم) ← **Advanced settings** ← **Install Extension…**، واختر الملف واتبع الخطوات. الإضافة غير موقّعة رقميًا بعد، لذا قد يظهر تحذير.
4. في إعدادات الإضافة اختر السماح بالتعديلات وبسكربتات C# ومدة الانتظار. تُكتب هذه القيم في `config.json` (أدناه).
5. شغّل Revit/AutoCAD. في كل مرة يشغّل فيها Claude الإضافة، تنسخ الإضافات لكل سنة مثبّتة في `C:\Program Files\Autodesk`، وتنسخ الملفات المتغيرة فقط. اطلب من Claude *"List my running Autodesk instances."*: يُظهر `addinSetup` ما تم تثبيته وأي `problems` (مثل ملف DLL مستخدم لأن Revit كان مفتوحًا: أغلقه وأعد تشغيل Claude).

للتحديث، ثبّت ملف .mcpb الجديد بالطريقة نفسها. إزالة الإضافة من Claude لا تزيل إضافات Revit/AutoCAD؛ استخدم `install.ps1 -Uninstall` من ملف zip لذلك. استخدم طريقة واحدة فقط على كل جهاز: الإضافة أو سكربت التثبيت أدناه.

**البديل: ملف zip وسكربت التثبيت.**

التثبيت، مع تسجيل الدخول بحساب الشخص الذي سيستخدمه:

1. انقر بزر الفأرة الأيمن على ملف zip ← **Properties** ← فعّل **Unblock** إن ظهر ← **OK**. افعل ذلك قبل فك الضغط؛ فبرامج 2022–2024 قد ترفض ملفات DLL المحظورة.
2. فُكّ الضغط، مثلًا إلى `C:\AutodeskMcp`.
3. أغلق Revit وAutoCAD.
4. في شريط العنوان للمجلد اكتب `powershell` واضغط Enter، ثم شغّل:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
```

بدون خيارات، يثبّت لكل إصدارات Revit وAutoCAD الموجودة في `C:\Program Files\Autodesk`. للاختيار أضف سنة واحدة في كل مرة، مثل `-RevitYears 2024` أو `-AutoCADYears 2026`، أو تجاوز برنامجًا بـ `-SkipRevit` أو `-SkipAutoCAD`.

الربط بـ Claude Desktop: افتح **Settings ← Developer ← Edit Config** وأضف التالي إلى `claude_desktop_config.json` (استبدل `<user>` باسم مستخدم Windows؛ يطبع برنامج التثبيت المسار الدقيق):

```json
{
  "mcpServers": {
    "autodesk": { "command": "C:\\Users\\<user>\\AppData\\Local\\AutodeskMcp\\server\\AutodeskMcp.Server.exe" }
  }
}
```

ثم أغلق Claude بالكامل (أيقونة شريط المهام ← Quit) وافتحه من جديد. لـ Claude Code، شغّل سطر `claude mcp add autodesk -- "…\AutodeskMcp.Server.exe"` الذي يطبعه برنامج التثبيت.

التحديث: نزّل ملف zip الجديد، وفعّل Unblock وفُكّ الضغط، وأغلق Revit وAutoCAD وClaude، ثم شغّل أمر التثبيت نفسه. لا يتغير إعداد Claude.

الإزالة:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Uninstall
```

ثم احذف إدخال `autodesk` من إعداد Claude. تبقى الإعدادات والسجلات في `%LOCALAPPDATA%\AutodeskMcp` حتى تحذف هذا المجلد.

### الإعدادات والأمان وأماكن الملفات

السياسة محفوظة في `%LOCALAPPDATA%\AutodeskMcp\config.json`. أنشئ الملف إن لم يكن موجودًا؛ يقرؤه Revit وAutoCAD عند التشغيل، لذا أعد تشغيلهما بعد أي تغيير. الإضافة هي التي تطبّق السياسة، فلا يستطيع Claude تجاوزها.

مع إضافة Claude Desktop، غيّر `allowModifications` و`allowCodeExecution` و`mainThreadTimeoutSeconds` من إعدادات الإضافة بدلًا من ذلك: فهي تكتبها في هذا الملف في كل مرة يبدأ فيها Claude، فتُستبدَل أي تعديلات يدوية على هذه المفاتيح الثلاثة.

```json
{
  "enabled": true,
  "allowModifications": true,
  "allowCodeExecution": true,
  "mainThreadTimeoutSeconds": 120
}
```

| الإعداد | الافتراضي | الأثر عند التغيير |
| --- | --- | --- |
| `enabled` | true | false: لا تقبل الإضافة أي اتصال |
| `allowModifications` | true | false: قراءة فقط؛ تُرفض كل أداة تغيّر النموذج أو الرسم |
| `allowCodeExecution` | true | false: تُرفض سكربتات C# |
| `mainThreadTimeoutSeconds` | 120 | مدة انتظار الطلب لبرنامج مشغول قبل إلغائه (لا يُنفَّذ متأخرًا أبدًا) |

بداية آمنة لمعظم المستخدمين هي `allowCodeExecution: false`؛ فعّلها لمن يحتاج السكربتات.

الأمان:

- الاتصال عبر Named Pipe محلي يقبل مستخدم Windows الحالي فقط، وكل طلب يحمل رمزًا عشوائيًا بطول 256-bit محفوظًا في ملف المستخدم. لا يستمع أي شيء على الشبكة.
- يجب أن يعمل Claude وRevit/AutoCAD بمستوى الصلاحيات نفسه؛ البرنامج الذي يعمل كمسؤول لا يمكن الوصول إليه من Claude يعمل بشكل عادي، والعكس صحيح.
- سكربتات C# تعمل بصلاحيات المستخدم في Windows داخل Revit أو AutoCAD.

| العنصر | المكان |
| --- | --- |
| خادم MCP | `%LOCALAPPDATA%\AutodeskMcp\server\AutodeskMcp.Server.exe` |
| إضافة Revit | `%APPDATA%\Autodesk\Revit\Addins\<year>\AutodeskMcp.addin` و `AutodeskMcp\` |
| حزمة AutoCAD | `%APPDATA%\Autodesk\ApplicationPlugins\AutodeskMcp.bundle` (حزمة واحدة لكل السنوات) |
| السياسة | `%LOCALAPPDATA%\AutodeskMcp\config.json` |
| البرامج المفتوحة | `%LOCALAPPDATA%\AutodeskMcp\instances\` (ملف لكل Revit/AutoCAD مفتوح) |
| السجلات | `%LOCALAPPDATA%\AutodeskMcp\logs\` |

الشيفرة المصدرية والإصدارات: [github.com/s81/Autodesk--mcp](https://github.com/s81/Autodesk--mcp).

</div>
