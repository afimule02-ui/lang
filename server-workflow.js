"use strict";

let serverWorkspace = null;
let serverDraftSaveTimer = null;
let serverDraftSavePromise = null;
let serverDraftSaveAgain = false;
let serverDraftAudioUploaded = false;
let coverContent = {
  vision: {
    am: "የአፍሪካ እያንዳንዱ ድምፅ በዲጂታል ዓለም ውስጥ ቦታ እንዲኖረው።",
    en: "Every African voice deserves a place in the digital world."
  },
  mission: {
    am: "የአፍሪካን ቋንቋዎች በፈቃድ የተመሠረተ ድምፅ፣ ጽሑፍ፣ ትርጉምና ጥራት ማረጋገጫ ሥራ እንሰበስባለን።",
    en: "We collect consent-led audio, transcription, translation, and quality-reviewed data across Africa's languages."
  }
};

function installServerTranslations() {
  Object.assign(AM, {
    "Every African voice deserves a place in the digital world.": "የአፍሪካ እያንዳንዱ ድምፅ በዲጂታል ዓለም ውስጥ ቦታ እንዲኖረው።",
    "We collect consent-led audio, transcription, translation, and quality-reviewed data across Africa's languages.": "በአፍሪካ ቋንቋዎች የድምፅ ቀረጻ፣ ጽሑፍ፣ ትርጉምና የጥራት ግምገማ በፈቃድ መሠረት እንሰበስባለን።",
    "Contributor register": "የተሳታፊ ምዝገባ",
    "Contributor login": "የተሳታፊ መግቢያ",
    "Agent login": "የኤጀንት መግቢያ",
    "Admin login": "የአስተዳዳሪ መግቢያ",
    "Choose your account": "የመለያ አይነት ይምረጡ",
    "Account sign in": "ወደ መለያ ይግቡ",
    "Sign in with your registered account.": "በተመዘገበ መለያዎ ይግቡ።",
    "Server data": "የሰርቨር መረጃ",
    "Server data only": "የሰርቨር መረጃ ብቻ",
    "Server request failed.": "የሰርቨር ጥያቄው አልተሳካም።",
    "Could not complete this request. Please retry.": "ጥያቄውን ማጠናቀቅ አልተቻለም። እንደገና ይሞክሩ።",
    "Could not load your server workspace.": "የሰርቨር የሥራ ቦታዎን መጫን አልተቻለም።",
    "Loading your workspace…": "የሥራ ቦታዎ በመጫን ላይ…",
    "New work": "አዲስ ሥራ",
    "Started work": "የተጀመረ ሥራ",
    "Completed work": "የተጠናቀቀ ሥራ",
    "Payment requests": "የክፍያ ጥያቄዎች",
    "Support": "ድጋፍ",
    "Start new work": "አዲስ ሥራ ይጀምሩ",
    "Continue saved work": "የተቀመጠ ሥራ ይቀጥሉ",
    "Saved drafts": "የተቀመጡ ረቂቆች",
    "No saved drafts yet.": "እስካሁን የተቀመጠ ረቂቅ የለም።",
    "Resume draft": "ረቂቁን ይቀጥሉ",
    "Delete draft": "ረቂቁን ይሰርዙ",
    "Draft saved to your account.": "ረቂቁ በመለያዎ ላይ ተቀምጧል።",
    "Work saved as a draft on the server.": "ሥራው በሰርቨሩ ላይ እንደ ረቂቅ ተቀምጧል።",
    "No work has been submitted yet.": "እስካሁን ሥራ አልተላከም።",
    "No completed work yet.": "እስካሁን የተጠናቀቀ ሥራ የለም።",
    "No payment requests yet.": "እስካሁን የክፍያ ጥያቄ የለም።",
    "No support cases yet.": "እስካሁን የድጋፍ ጉዳይ የለም።",
    "Available balance": "ያለ ባላንስ",
    "Approved work earns the amount set by an administrator. A payout is not marked paid until the administrator records proof.": "የጸደቀ ሥራ ለእያንዳንዱ ሥራ አስተዳዳሪው የመደበውን ገንዘብ ያስገኛል። አስተዳዳሪው ማስረጃ እስኪመዘግብ ክፍያ እንደተፈጸመ አይታይም።",
    "Request payout": "ክፍያ ይጠይቁ",
    "Amount (ETB)": "መጠን (ብር)",
    "Requests must be 1,000–10,000 ETB and cannot exceed your approved balance.": "ጥያቄው ከ1,000 እስከ 10,000 ብር መሆን አለበት፤ የጸደቀውን ባላንስ ማለፍ አይችልም።",
    "Payout request sent.": "የክፍያ ጥያቄው ተልኳል።",
    "Payout status": "የክፍያ ሁኔታ",
    "This page records requests and proof only. It does not send money.": "ይህ ገጽ ጥያቄንና ማስረጃን ብቻ ይመዘግባል፤ ገንዘብ አይልክም።",
    "Pending agent review": "የኤጀንት ግምገማ በመጠባበቅ ላይ",
    "Pending admin review": "የአስተዳዳሪ ግምገማ በመጠባበቅ ላይ",
    "Pending admin review": "የአስተዳዳሪ ግምገማ በመጠባበቅ ላይ",
    "Approved": "ጸድቋል",
    "Paid": "ተከፍሏል",
    "Rejected": "ውድቅ ተደርጓል",
    "Returned by agent": "በኤጀንት ተመልሷል",
    "Awaiting agent review": "የኤጀንት ግምገማ በመጠባበቅ ላይ",
    "Awaiting admin review": "የአስተዳዳሪ ግምገማ በመጠባበቅ ላይ",
    "Returned by admin": "በአስተዳዳሪ ለማስተካከያ ተመልሷል",
    "In progress": "በሂደት ላይ",
    "Agent approved work; final approval is still pending with the administrator.": "ኤጀንቱ ሥራውን አጽድቋል፤ የመጨረሻ ፈቃድ ከአስተዳዳሪው ይጠበቃል።",
    "Your work": "የእርስዎ ሥራ",
    "Team members": "የቡድን አባላት",
    "Your referral link": "የሪፈራል ሊንክዎ",
    "Referral code": "የሪፈራል ኮድ",
    "Copy link": "ሊንኩን ቅዳ",
    "Registration date": "የምዝገባ ቀን",
    "No contributors are linked to your referral yet.": "እስካሁን በሪፈራልዎ የተገናኘ ተሳታፊ የለም።",
    "Agent review is not final approval. Work goes to an administrator after you accept it.": "የኤጀንት ግምገማ የመጨረሻ ማጽደቅ አይደለም። ሲቀበሉት ሥራው ወደ አስተዳዳሪ ይሄዳል።",
    "Accept and send to admin": "ተቀብለው ለአስተዳዳሪ ይላኩ",
    "Return to contributor": "ለተሳታፊው ይመልሱ",
    "Correction comment": "የማስተካከያ አስተያየት",
    "Add a correction comment before returning this work.": "ሥራውን ከመመለስዎ በፊት የማስተካከያ አስተያየት ያስገቡ።",
    "Final quality review": "የመጨረሻ የጥራት ግምገማ",
    "Approve final work": "የመጨረሻ ሥራውን አጽድቅ",
    "Return for correction": "ለማስተካከያ መልስ",
    "Reject with reason": "ከምክንያት ጋር ውድቅ አድርግ",
    "Award amount (ETB)": "የሽልማት መጠን (ብር)",
    "Final approval creates an unpaid balance. It does not transfer money.": "የመጨረሻ ማጽደቅ የሚከፈል ባላንስ ይፈጥራል፤ ገንዘብ አያስተላልፍም።",
    "Contributor and Agent accounts": "የተሳታፊና የኤጀንት መለያዎች",
    "Final review": "የመጨረሻ ግምገማ",
    "Reports": "ሪፖርቶች",
    "Cover page text": "የመነሻ ገጽ ጽሑፍ",
    "Event log": "የክስተት መዝገብ",
    "Mission": "ተልዕኮ",
    "Vision": "ራእይ",
    "Vision in Amharic": "ራእይ በአማርኛ",
    "Vision in English": "ራእይ በእንግሊዝኛ",
    "Mission in Amharic": "ተልዕኮ በአማርኛ",
    "Mission in English": "ተልዕኮ በእንግሊዝኛ",
    "Save cover text": "የመነሻ ገጽ ጽሑፍ አስቀምጥ",
    "Cover page text saved.": "የመነሻ ገጽ ጽሑፍ ተቀምጧል።",
    "All reports use saved server records.": "ሪፖርቶች በሰርቨሩ ላይ በተቀመጡ መዝገቦች ላይ የተመሠረቱ ናቸው።",
    "Collected audio hours": "የተሰበሰቡ የድምፅ ሰዓታት",
    "Submissions": "የተላኩ ሥራዎች",
    "Audio hours": "የድምፅ ሰዓታት",
    "By language": "በቋንቋ",
    "By agent": "በኤጀንት",
    "By contributor": "በተሳታፊ",
    "By date": "በቀን",
    "Work status": "የሥራ ሁኔታ",
    "Assigned agent": "የተመደበ ኤጀንት",
    "Account role": "የመለያ ሚና",
    "Active account": "ንቁ መለያ",
    "Save account changes": "የመለያ ለውጦችን አስቀምጥ",
    "Account updated.": "የመለያ መረጃ ተሻሽሏል።",
    "No accounts found.": "መለያ አልተገኘም።",
    "Support case": "የድጋፍ ጉዳይ",
    "Subject": "ርዕስ",
    "Describe the issue": "ችግሩን ይግለጹ",
    "Open support case": "የድጋፍ ጉዳይ ክፈት",
    "Send message": "መልዕክት ላክ",
    "Write a reply": "ምላሽ ይጻፉ",
    "Message sent.": "መልዕክቱ ተልኳል።",
    "Support case created.": "የድጋፍ ጉዳዩ ተፈጥሯል።",
    "Open": "ክፈት",
    "In progress": "በሂደት ላይ",
    "Escalated": "ወደ አስተዳዳሪ ተላክ",
    "Closed": "ተዘግቷል",
    "Escalate to admin": "ወደ አስተዳዳሪ አሳድግ",
    "Update support case": "የድጋፍ ጉዳዩን አዘምን",
    "Payment proof reference": "የክፍያ ማስረጃ ማጣቀሻ",
    "Approve payout request": "የክፍያ ጥያቄውን አጽድቅ",
    "Reject payout request": "የክፍያ ጥያቄውን ውድቅ አድርግ",
    "Mark as paid with proof": "ማስረጃ ካለ እንደተከፈለ መዝግብ",
    "Payout approved; payment has not been sent.": "የክፍያ ጥያቄው ጸድቋል፤ ገንዘቡ ገና አልተላከም።",
    "Payout marked paid with proof.": "ክፍያው በማስረጃ እንደተፈጸመ ተመዝግቧል።",
    "Unassigned": "ያልተመደበ",
    "None": "የለም",
    "Refresh data": "መረጃውን አድስ",
    "Show transcript and translation": "ጽሑፉንና ትርጉሙን አሳይ",
    "Duration": "ቆይታ",
    "Contributor": "ተሳታፊ",
    "Agent": "ኤጀንት",
    "Administrator": "አስተዳዳሪ",
    "Registered": "የተመዘገበበት ቀን",
    "Role": "ሚና",
    "Status": "ሁኔታ",
    "Language": "ቋንቋ",
    "Reason": "ምክንያት",
    "Payment status": "የክፍያ ሁኔታ",
    "Requested": "የተጠየቀበት ቀን",
    "Proof": "ማስረጃ",
    "User has no access to this support case.": "ተጠቃሚው ይህን የድጋፍ ጉዳይ የማየት ፈቃድ የለውም።",
    "Unable to save the draft. Your local text remains on this device.": "ረቂቁን ማስቀመጥ አልተቻለም። ጽሑፉ በዚህ መሣሪያ ላይ አለ።",
    "Server-backed workflow": "በሰርቨር የሚተዳደር የሥራ ፍሰት",
    "Open work": "ክፍት ሥራዎች",
    "Need correction": "ማስተካከያ የሚፈልግ",
    "Assigned team": "የተመደበ ቡድን",
    "People": "ሰዎች",
    "Hours": "ሰዓታት",
    "No events recorded yet.": "እስካሁን ክስተት አልተመዘገበም።",
    "No support cases found.": "የድጋፍ ጉዳይ አልተገኘም።",
    "No work is waiting in this queue.": "በዚህ ወረፋ ላይ የሚጠብቅ ሥራ የለም።",
    "Start recording": "ቀረጻ ይጀምሩ",
    "Download event log": "የክስተት መዝገብ ያውርዱ",
    "New support message": "አዲስ የድጋፍ መልዕክት",
    "Server data only": "የሰርቨር ውሂብ ብቻ",
    "No payment balance is available yet.": "እስካሁን ለክፍያ የሚገኝ ባላንስ የለም።",
    "A user can only be linked to an active agent.": "ተጠቃሚ ከንቁ ኤጀንት ጋር ብቻ ሊገናኝ ይችላል።",
    "Role and access changes are applied by the server.": "የሚናና የመዳረሻ ለውጦች በሰርቨሩ ላይ ይተገበራሉ።",
    "Final administrator approval is required.": "የአስተዳዳሪ የመጨረሻ ፈቃድ ያስፈልጋል።",
    "No admin-only data is available outside the admin role.": "ለአስተዳዳሪ ብቻ የተፈቀደው መረጃ ከዚያ ሚና ውጭ አይታይም።",
    "The server declined this request.": "ሰርቨሩ ይህን ጥያቄ አልተቀበለም።"
  });
}

function currentSiteContent() {
  return coverContent;
}

function serverErrorToast(error) {
  const message = String(error?.message || "");
  toast(AM[message] ? message : "Could not complete this request. Please retry.");
}

async function serverFetch(path, options = {}) {
  const response = await fetch(apiBaseUrl() + path, { credentials: "include", ...options });
  let body = {};
  try { body = await response.json(); } catch { /* the response may be an audio or empty response */ }
  if (!response.ok) throw new Error(body.detail || "Server request failed.");
  return body;
}

function serverJson(method, value) {
  return { method, headers: { "Content-Type": "application/json" }, body: JSON.stringify(value) };
}

async function loadCoverContent() {
  try {
    const content = await serverFetch("/api/site-content");
    if (content?.vision?.am && content?.vision?.en && content?.mission?.am && content?.mission?.en) {
      coverContent = content;
      if (state.view === "home") render("home");
    }
  } catch { /* the built-in bilingual text remains available when the API is offline */ }
}

async function loadServerWorkspace() {
  const data = await serverFetch("/api/workspace");
  serverWorkspace = data;
  if (data.role !== state.role) throw new Error("This account does not have access to this sign-in page.");
  if (data.referralCode) state.agentCode = data.referralCode;
  if (data.siteContent) coverContent = data.siteContent;
  return data;
}

function serverLoading() {
  return '<section class="workspace-card"><p role="status">' + tr("Loading your workspace…") + '</p></section>';
}

function serverTabBar(tabs, selected, kind) {
  return '<nav class="server-tab-bar" aria-label="' + tr("Server-backed workflow") + '">' + tabs.map(([id, label]) =>
    '<button type="button" class="filter-chip ' + (selected === id ? 'selected' : '') + '" data-server-tab-kind="' + kind + '" data-server-tab="' + esc(id) + '" aria-pressed="' + (selected === id) + '">' + tr(label) + '</button>'
  ).join("") + '</nav>';
}

function serverStatusClass(status) {
  if (status === "Approved" || status === "Paid") return "approved";
  if (status.startsWith("Returned") || status === "Rejected") return "pending";
  if (status.includes("review")) return "review";
  if (status === "In progress" || status === "Escalated") return "progress";
  return "";
}

function serverSubmissionCard(item, mode = "view") {
  const status = esc(item.status || "In progress");
  const audio = item.audioUrl ? '<audio controls preload="metadata" src="' + esc(apiBaseUrl() + item.audioUrl) + '" aria-label="' + tr("Audio player") + '"></audio>' : '';
  let actions = '';
  if (mode === "agent" && ["Awaiting agent review", "Returned for correction"].includes(item.status)) {
    actions = '<label>' + tr("Correction comment") + '<textarea data-server-agent-note="' + item.id + '" maxlength="4000" placeholder="' + tr("Explain what needs correction") + '"></textarea></label><div class="review-actions"><button class="primary-button" type="button" data-server-action="agent-review-accept" data-id="' + item.id + '">' + tr("Accept and send to admin") + '</button><button class="secondary-button" type="button" data-server-action="agent-review-return" data-id="' + item.id + '">' + tr("Return to contributor") + '</button></div>';
  }
  if (mode === "admin" && ["Awaiting admin review", "Awaiting review", "Returned by admin"].includes(item.status)) {
    actions = '<div class="server-review-fields"><label>' + tr("Correction comment") + '<textarea data-server-admin-comment="' + item.id + '" maxlength="4000"></textarea></label><label>' + tr("Reason") + '<textarea data-server-admin-reason="' + item.id + '" maxlength="4000"></textarea></label><label>' + tr("Award amount (ETB)") + '<input type="number" min="0" step="0.01" data-server-award="' + item.id + '"></label></div><div class="review-actions"><button class="primary-button" type="button" data-server-action="admin-review-approve" data-id="' + item.id + '">' + tr("Approve final work") + '</button><button class="secondary-button" type="button" data-server-action="admin-review-return" data-id="' + item.id + '">' + tr("Return for correction") + '</button><button class="danger-button" type="button" data-server-action="admin-review-reject" data-id="' + item.id + '">' + tr("Reject with reason") + '</button></div>';
  }
  return '<article class="workspace-card server-work-card"><header class="server-work-heading"><div><h3>' + esc(item.language || tr("Language")) + ' · #' + item.id + '</h3><p>' + esc(item.contributorName || state.user || "") + (item.contributorPhone ? ' · ' + esc(item.contributorPhone) : '') + ' · ' + tr("Duration") + ': ' + formatTime(Math.max(0, Number(item.durationSeconds) || 0)) + '</p></div><span class="status-pill ' + serverStatusClass(item.status || "") + '">' + tr(status) + '</span></header>' + audio + '<details class="server-transcript-details"><summary>' + tr("Show transcript and translation") + '</summary><h4>' + tr("Transcript") + '</h4><p class="server-long-text">' + esc(item.transcript || "") + '</p><h4>' + tr("Translation") + '</h4><p class="server-long-text">' + esc(item.translation || "") + '</p>' + (item.agentComment ? '<p><b>' + tr("Agent") + ':</b> ' + esc(item.agentComment) + '</p>' : '') + (item.adminComment ? '<p><b>' + tr("Administrator") + ':</b> ' + esc(item.adminComment) + '</p>' : '') + (item.rejectionReason ? '<p><b>' + tr("Reason") + ':</b> ' + esc(item.rejectionReason) + '</p>' : '') + '</details>' + (item.awardAmount ? '<p><b>' + tr("Available balance") + ':</b> ' + Number(item.awardAmount).toFixed(2) + ' ' + esc(item.currency || "ETB") + '</p>' : '') + actions + '</article>';
}

function serverSupportList(tickets, mode) {
  if (!tickets?.length) return '<p class="demo-note">' + tr("No support cases yet.") + '</p>';
  const users = serverWorkspace?.users || [];
  return tickets.map(ticket => {
    const messages = (ticket.messages || []).map(message => '<div class="chat-message ' + (message.userId === state.serverUserId ? 'mine' : '') + '"><b>' + esc(message.name) + ' · ' + tr(message.role === "admin" ? "Administrator" : message.role === "agent" ? "Agent" : "Contributor") + '</b><p>' + esc(message.body) + '</p><small>' + new Date(message.createdAt).toLocaleString(state.lang === "am" ? "am-ET" : "en-US") + '</small></div>').join("");
    const adminControls = mode === "admin" ? '<form class="server-support-state" data-server-form="ticket-admin" data-ticket-id="' + ticket.id + '"><select name="status"><option>Open</option><option>In progress</option><option>Escalated</option><option>Closed</option></select><select name="assignedAgentId"><option value="">' + tr("Unassigned") + '</option>' + users.filter(x => x.role === "agent" && x.active).map(x => '<option value="' + x.id + '" ' + (x.id === ticket.assignedAgentId ? 'selected' : '') + '>' + esc(x.name) + '</option>').join("") + '</select><button class="secondary-button">' + tr("Update support case") + '</button></form>' : mode === "agent" && ticket.assignedAgentId === state.serverUserId ? '<button type="button" class="secondary-button" data-server-action="ticket-escalate" data-id="' + ticket.id + '">' + tr("Escalate to admin") + '</button>' : '';
    const reply = ticket.status !== "Closed" ? '<form class="server-message-form" data-server-form="ticket-message" data-ticket-id="' + ticket.id + '"><input name="message" maxlength="4000" required placeholder="' + tr("Write a reply") + '"><button class="primary-button">' + tr("Send message") + '</button></form>' : '';
    return '<article class="workspace-card server-ticket"><header class="server-work-heading"><div><h3>' + esc(ticket.subject) + ' · #' + ticket.id + '</h3><p>' + esc(ticket.createdByName) + (ticket.createdByPhone ? ' · ' + esc(ticket.createdByPhone) : '') + '</p></div><span class="status-pill ' + serverStatusClass(ticket.status) + '">' + tr(ticket.status) + '</span></header><div class="chat-thread" aria-live="polite">' + messages + '</div>' + reply + adminControls + '</article>';
  }).join("");
}

function serverPaymentTable(requests, mode) {
  if (!requests?.length) return '<p class="demo-note">' + tr("No payment requests yet.") + '</p>';
  return requests.map(payment => {
    let actions = '';
    if (mode === "agent" && payment.status === "Pending agent review" && payment.userId !== state.serverUserId) actions = '<label>' + tr("Reason") + '<input data-server-payment-note="' + payment.id + '"></label><div class="review-actions"><button class="primary-button" data-server-action="payment-agent-accept" data-id="' + payment.id + '">' + tr("Approve payout request") + '</button><button class="secondary-button" data-server-action="payment-agent-return" data-id="' + payment.id + '">' + tr("Return payout request") + '</button></div>';
    if (mode === "admin" && ["Pending admin review", "Returned by agent"].includes(payment.status)) actions = '<label>' + tr("Reason") + '<input data-server-payment-note="' + payment.id + '"></label><div class="review-actions"><button class="primary-button" data-server-action="payment-admin-approve" data-id="' + payment.id + '">' + tr("Approve payout request") + '</button><button class="secondary-button" data-server-action="payment-admin-reject" data-id="' + payment.id + '">' + tr("Reject payout request") + '</button></div>';
    if (mode === "admin" && payment.status === "Approved") actions = '<label>' + tr("Payment proof reference") + '<input data-server-payment-proof="' + payment.id + '" required></label><button class="primary-button" data-server-action="payment-admin-paid" data-id="' + payment.id + '">' + tr("Mark as paid with proof") + '</button>';
    return '<article class="payment-request server-payment-row"><div><h3>' + esc(payment.userName || state.user || tr("Contributor")) + ' · ' + Number(payment.amount).toFixed(2) + ' ' + esc(payment.currency || "ETB") + '</h3><p>' + tr("Requested") + ': ' + new Date(payment.requestedAt).toLocaleString(state.lang === "am" ? "am-ET" : "en-US") + '</p>' + (payment.reviewNote ? '<p>' + tr("Reason") + ': ' + esc(payment.reviewNote) + '</p>' : '') + (payment.proofReference ? '<p>' + tr("Proof") + ': ' + esc(payment.proofReference) + '</p>' : '') + '</div><span class="status-pill ' + serverStatusClass(payment.status) + '">' + tr(payment.status) + '</span>' + actions + '</article>';
  }).join("");
}

function serverPayoutForm(balance) {
  return '<section class="workspace-card payout-card"><div class="panel-heading"><div><h2>' + tr("Payment requests") + '</h2><p>' + tr("This page records requests and proof only. It does not send money.") + '</p></div><div class="wallet-balance"><div><small>' + tr("Available balance") + '</small><strong>' + Number(balance || 0).toFixed(2) + ' ETB</strong></div><span aria-hidden="true">+</span></div></div><p class="demo-note">' + tr("Requests must be 1,000–10,000 ETB and cannot exceed your approved balance.") + '</p><form class="server-payout-form" data-server-form="payout-request"><label>' + tr("Amount (ETB)") + '<input name="amount" type="number" min="1000" max="10000" step="0.01" required ' + (Number(balance || 0) < 1000 ? 'disabled' : '') + '></label><button class="primary-button" ' + (Number(balance || 0) < 1000 ? 'disabled' : '') + '>' + tr("Request payout") + '</button></form></section>';
}

function serverContributorDashboard() {
  state.title = "My dashboard";
  if (!serverWorkspace || serverWorkspace.role !== "contributor") return serverLoading();
  const w = serverWorkspace, tabs = [["new", "New work"], ["started", "Started work"], ["completed", "Completed work"], ["payments", "Payment requests"], ["support", "Support"]];
  const selected = tabs.some(([id]) => id === state.serverContributorTab) ? state.serverContributorTab : "new";
  state.serverContributorTab = selected;
  const submissions = w.submissions || [], drafts = w.drafts || [];
  const content = selected === "new" ? '<section class="workspace-card"><p>' + tr("Choose a country and language, register two or more speakers, then record, transcribe, translate, and review your work.") + '</p><button class="primary-button" data-server-action="start-work">' + tr("Start new work") + '</button></section>' :
    selected === "started" ? '<section class="workspace-card"><h2>' + tr("Saved drafts") + '</h2>' + (drafts.length ? drafts.map(d => '<article class="server-draft-row"><div><b>' + esc(d.language) + '</b><p>' + (d.hasAudio ? tr("Audio player") + ' · ' + formatTime(d.durationSeconds) : tr("No recording yet")) + ' · ' + new Date(d.updatedAt).toLocaleString(state.lang === "am" ? "am-ET" : "en-US") + '</p></div><div><button class="primary-button" data-server-action="resume-draft" data-id="' + d.id + '">' + tr("Resume draft") + '</button> <button class="danger-button" data-server-action="delete-draft" data-id="' + d.id + '">' + tr("Delete draft") + '</button></div></article>').join("") : '<p>' + tr("No saved drafts yet.") + '</p>') + '</section><section class="server-work-list">' + (submissions.filter(x => !["Approved", "Rejected"].includes(x.status)).map(x => serverSubmissionCard(x)).join("") || '<p>' + tr("No work has been submitted yet.") + '</p>') + '</section>' :
    selected === "completed" ? '<section class="server-work-list">' + (submissions.filter(x => x.status === "Approved").map(x => serverSubmissionCard(x)).join("") || '<p>' + tr("No completed work yet.") + '</p>') + '</section>' :
    selected === "payments" ? serverPayoutForm(w.balance) + '<section class="server-payment-list">' + serverPaymentTable(w.paymentRequests || [], "contributor") + '</section>' :
    '<section class="workspace-card"><h2>' + tr("Support") + '</h2><form class="server-support-create" data-server-form="ticket-create"><label>' + tr("Subject") + '<input name="subject" maxlength="200" required></label><label>' + tr("Describe the issue") + '<textarea name="message" maxlength="4000" required></textarea></label><button class="primary-button">' + tr("Open support case") + '</button></form></section><section class="server-support-list">' + serverSupportList(w.supportTickets || [], "contributor") + '</section>';
  return heading("CONTRIBUTOR", "My dashboard", "Your work, drafts, payment requests, and support") + '<div class="server-workspace-badge">' + tr("Server data only") + '</div>' + serverTabBar(tabs, selected, "contributor") + content;
}

function serverAgentDashboard() {
  state.title = "Agent / focal person";
  if (!serverWorkspace || serverWorkspace.role !== "agent") return serverLoading();
  const w = serverWorkspace, tabs = [["team", "Team members"], ["review", "Review queues"], ["support", "Support"], ["payments", "Payment requests"]];
  const selected = tabs.some(([id]) => id === state.serverAgentTab) ? state.serverAgentTab : "team";
  state.serverAgentTab = selected;
  const referral = new URL(location.href); referral.search = ""; referral.hash = ""; referral.searchParams.set("ref", w.referralCode || "");
  let content = '';
  if (selected === "team") {
    content = '<section class="workspace-card"><h2>' + tr("Your referral link") + '</h2><div class="referral-row"><input id="agentReferral" readonly value="' + esc(referral.href) + '"><button type="button" class="secondary-button" data-action="copy-referral">' + tr("Copy link") + '</button></div><p><b>' + tr("Referral code") + ':</b> ' + esc(w.referralCode) + '</p></section><section class="workspace-card"><h2>' + tr("Team members") + '</h2>' + ((w.contributors || []).length ? '<div class="table-wrap"><table class="finance-table"><thead><tr><th>' + tr("Contributor") + '</th><th>' + tr("Phone number") + '</th><th>' + tr("Registration date") + '</th><th>' + tr("Submissions") + '</th></tr></thead><tbody>' + w.contributors.map(p => '<tr><td>' + esc(p.name) + '</td><td><a href="tel:' + esc(p.phone) + '">' + esc(p.phone) + '</a></td><td>' + new Date(p.registeredAt).toLocaleDateString(state.lang === "am" ? "am-ET" : "en-US") + '</td><td>' + (w.submissions || []).filter(s => s.contributorId === p.id).length + '</td></tr>').join("") + '</tbody></table></div>' : '<p>' + tr("No contributors are linked to your referral yet.") + '</p>') + '</section><section class="workspace-card"><h2>' + tr("Your work") + '</h2><p>' + tr("Available balance") + ': <b>' + Number(w.balance || 0).toFixed(2) + ' ETB</b></p>' + tr("Agent review is not final approval. Work goes to an administrator after you accept it.") + '</section>';
  } else if (selected === "review") {
    const queue = (w.submissions || []).filter(x => x.status === "Awaiting agent review" || x.status === "Returned for correction");
    content = '<section class="workspace-card"><p>' + tr("Agent review is not final approval. Work goes to an administrator after you accept it.") + '</p></section><section class="server-work-list">' + (queue.map(x => serverSubmissionCard(x, "agent")).join("") || '<p>' + tr("No work is waiting in this queue.") + '</p>') + '</section>';
  } else if (selected === "support") {
    content = '<section class="workspace-card"><p>' + tr("Support") + '</p></section><section class="server-support-list">' + serverSupportList(w.supportTickets || [], "agent") + '</section>';
  } else {
    content = serverPayoutForm(w.balance) + '<section class="server-payment-list">' + serverPaymentTable(w.paymentRequests || [], "agent") + '</section>';
  }
  return heading("AGENT", "Agent workspace", "Manage your server-linked team and review their work") + '<div class="server-workspace-badge">' + tr("Server data only") + '</div>' + serverTabBar(tabs, selected, "agent") + content;
}

function serverAdminDashboard() {
  state.title = "Administration";
  if (!serverWorkspace || serverWorkspace.role !== "admin") return serverLoading();
  const tabs = [["overview", "Overview"], ["users", "Contributor and Agent accounts"], ["quality", "Final review"], ["support", "Support"], ["payments", "Payment requests"], ["reports", "Reports"], ["cover", "Cover page text"], ["events", "Event log"]];
  const selected = tabs.some(([id]) => id === state.serverAdminTab) ? state.serverAdminTab : "overview";
  state.serverAdminTab = selected;
  const w = serverWorkspace, items = w.submissions || [], report = w.report || {};
  let content = '';
  if (selected === "overview") {
    content = '<div class="metrics-grid"><div class="metric-card"><span>' + tr("People") + '</span><strong>' + (w.users || []).length + '</strong></div><div class="metric-card"><span>' + tr("Collected audio hours") + '</span><strong>' + Number(report.audioHours || 0).toFixed(2) + '</strong></div><div class="metric-card"><span>' + tr("Awaiting admin review") + '</span><strong>' + items.filter(x => ["Awaiting admin review", "Awaiting review"].includes(x.status)).length + '</strong></div><div class="metric-card"><span>' + tr("Approved") + '</span><strong>' + items.filter(x => x.status === "Approved").length + '</strong></div></div><section class="workspace-card"><h2>' + tr("Work status") + '</h2><div class="server-work-list">' + (items.map(x => serverSubmissionCard(x)).join("") || '<p>' + tr("No work has been submitted yet.") + '</p>') + '</div></section>';
  } else if (selected === "users") {
    const agents = (w.users || []).filter(x => x.role === "agent");
    content = '<section class="workspace-card"><p>' + tr("Role and access changes are applied by the server.") + '</p><div class="server-user-list">' + ((w.users || []).map(user => '<form class="server-user-row" data-server-form="user-update" data-user-id="' + user.id + '"><div class="server-user-ident"><b>' + esc(user.name) + '</b><small>' + esc(user.phone) + ' · ' + new Date(user.registeredAt).toLocaleDateString(state.lang === "am" ? "am-ET" : "en-US") + (user.referralCode ? ' · ' + tr("Referral code") + ': ' + esc(user.referralCode) : '') + '</small></div><label>' + tr("Account role") + '<select name="role"><option value="contributor" ' + (user.role === "contributor" ? 'selected' : '') + '>' + tr("Contributor") + '</option><option value="agent" ' + (user.role === "agent" ? 'selected' : '') + '>' + tr("Agent") + '</option><option value="admin" ' + (user.role === "admin" ? 'selected' : '') + '>' + tr("Administrator") + '</option></select></label><label>' + tr("Assigned agent") + '<select name="agentId"><option value="">' + tr("Unassigned") + '</option>' + agents.map(agent => '<option value="' + agent.id + '" ' + (agent.id === user.agentId ? 'selected' : '') + '>' + esc(agent.name) + '</option>').join("") + '</select></label><label class="server-active-check"><input type="checkbox" name="active" ' + (user.active ? 'checked' : '') + '> ' + tr("Active account") + '</label><button class="secondary-button">' + tr("Save account changes") + '</button></form>').join("") || '<p>' + tr("No accounts found.") + '</p>') + '</div></section>';
  } else if (selected === "quality") {
    const queue = items.filter(x => ["Awaiting admin review", "Awaiting review", "Returned by admin"].includes(x.status));
    content = '<section class="workspace-card"><p>' + tr("Final administrator approval is required.") + '</p></section><section class="server-work-list">' + (queue.map(x => serverSubmissionCard(x, "admin")).join("") || '<p>' + tr("No work is waiting in this queue.") + '</p>') + '</section>';
  } else if (selected === "support") {
    content = '<section class="server-support-list">' + serverSupportList(w.supportTickets || [], "admin") + '</section>';
  } else if (selected === "payments") {
    content = '<section class="workspace-card"><p>' + tr("Payout approved; payment has not been sent.") + '</p></section><section class="server-payment-list">' + serverPaymentTable(w.paymentRequests || [], "admin") + '</section>';
  } else if (selected === "reports") {
    const table = (title, rows, heads, renderRow) => '<section class="workspace-card"><h2>' + tr(title) + '</h2><div class="table-wrap"><table class="finance-table"><thead><tr>' + heads.map(x => '<th>' + tr(x) + '</th>').join("") + '</tr></thead><tbody>' + (rows || []).map(renderRow).join("") + '</tbody></table></div></section>';
    content = '<section class="workspace-card"><p>' + tr("All reports use saved server records.") + '</p><h2>' + tr("Collected audio hours") + ': ' + Number(report.audioHours || 0).toFixed(2) + '</h2><p>' + tr("Submissions") + ': ' + (items.length || 0) + ' · ' + tr("Approved") + ': ' + (report.approved || 0) + ' · ' + tr("Returned") + ': ' + (report.returned || 0) + '</p></section>' + table("By language", report.byLanguage, ["Language", "Audio hours", "Submissions", "Approved", "Returned"], x => '<tr><td>' + esc(x.language) + '</td><td>' + x.audioHours + '</td><td>' + x.submissions + '</td><td>' + x.approved + '</td><td>' + x.returned + '</td></tr>') + table("By date", report.byDate, ["Date", "Audio hours", "Submissions", "Approved", "Returned"], x => '<tr><td>' + esc(x.date) + '</td><td>' + x.audioHours + '</td><td>' + x.submissions + '</td><td>' + x.approved + '</td><td>' + x.returned + '</td></tr>') + table("By agent", report.byAgent, ["Agent", "Audio hours", "Submissions", "Approved"], x => '<tr><td>' + esc(x.agent) + '</td><td>' + x.audioHours + '</td><td>' + x.submissions + '</td><td>' + x.approved + '</td></tr>') + table("By contributor", report.byContributor, ["Contributor", "Audio hours", "Submissions", "Approved"], x => '<tr><td>' + esc(x.contributor) + '</td><td>' + x.audioHours + '</td><td>' + x.submissions + '</td><td>' + x.approved + '</td></tr>');
  } else if (selected === "cover") {
    const c = w.siteContent || coverContent;
    content = '<section class="workspace-card"><h2>' + tr("Cover page text") + '</h2><form class="server-cover-form" data-server-form="site-content"><label>' + tr("Vision in Amharic") + '<textarea name="visionAm" maxlength="2000" required>' + esc(c.vision.am) + '</textarea></label><label>' + tr("Vision in English") + '<textarea name="visionEn" maxlength="2000" required>' + esc(c.vision.en) + '</textarea></label><label>' + tr("Mission in Amharic") + '<textarea name="missionAm" maxlength="4000" required>' + esc(c.mission.am) + '</textarea></label><label>' + tr("Mission in English") + '<textarea name="missionEn" maxlength="4000" required>' + esc(c.mission.en) + '</textarea></label><button class="primary-button">' + tr("Save cover text") + '</button></form></section>';
  } else {
    content = '<section class="workspace-card"><div class="panel-heading"><h2>' + tr("Event log") + '</h2><button class="secondary-button" data-server-action="export-events">' + tr("Download event log") + '</button></div>' + ((w.events || []).length ? '<div class="table-wrap"><table class="finance-table"><thead><tr><th>' + tr("Requested") + '</th><th>' + tr("Contributor") + '</th><th>' + tr("Role") + '</th><th>' + tr("Status") + '</th><th>' + tr("Details") + '</th></tr></thead><tbody>' + w.events.map(event => '<tr><td>' + new Date(event.createdAt).toLocaleString(state.lang === "am" ? "am-ET" : "en-US") + '</td><td>' + esc(event.actorId || "") + '</td><td>' + tr(event.role === "admin" ? "Administrator" : event.role === "agent" ? "Agent" : event.role === "contributor" ? "Contributor" : event.role) + '</td><td>' + esc(event.type) + ' · ' + esc(event.entity) + ' #' + esc(event.entityId) + '</td><td>' + esc(event.details) + '</td></tr>').join("") + '</tbody></table></div>' : '<p>' + tr("No events recorded yet.") + '</p>') + '</section>';
  }
  return heading("ADMIN", "Management dashboard", "Review server records, manage accounts, support, payments, and reports") + '<div class="server-workspace-badge">' + tr("Server data only") + '</div>' + serverTabBar(tabs, selected, "admin") + content;
}

function serverReviewPage() {
  state.title = "Review queues";
  return state.role === "admin" ? serverAdminDashboard() : serverAgentDashboard();
}

async function refreshServerView(view = state.view) {
  try { await loadServerWorkspace(); render(view); }
  catch (error) { serverErrorToast(error); }
}

async function startServerWork() {
  state.activeDraftId = null;
  state.drafts = {};
  state.participants = [];
  state.participantConsent = false;
  state.activeTaskId = null;
  serverDraftAudioUploaded = false;
  if (audioUrl) URL.revokeObjectURL(audioUrl);
  audioUrl = null;
  audioBlob = null;
  state.audioMeta = null;
  elapsed = 0;
  recordingState = "idle";
  render("project");
}

async function resumeServerDraft(id) {
  try {
    const draft = (serverWorkspace?.drafts || []).find(x => String(x.id) === String(id));
    if (!draft) throw new Error("The saved draft was not found.");
    state.activeDraftId = draft.id;
    state.drafts = { transcript: draft.transcript || "", translation: draft.translation || "", transcriptSegments: [], translationSegments: [] };
    try { state.participants = Array.isArray(draft.speakers) ? draft.speakers : []; } catch { state.participants = []; }
    state.participantConsent = true;
    state.consentRef = "CONSENT-TERMS-v1-AUDIO-v1";
    state.recordingType = draft.recordingType || "Conversation";
    elapsed = Math.max(0, Number(draft.durationSeconds) || 0);
    serverDraftAudioUploaded = !!draft.hasAudio;
    if (audioUrl) URL.revokeObjectURL(audioUrl);
    audioUrl = null;
    audioBlob = null;
    if (draft.hasAudio && draft.audioUrl) {
      const response = await fetch(apiBaseUrl() + draft.audioUrl, { credentials: "include" });
      if (!response.ok) throw new Error("The saved draft audio is not available.");
      audioBlob = await response.blob();
      audioUrl = URL.createObjectURL(audioBlob);
      recordingState = "completed";
      state.audioMeta = { duration: elapsed, size: audioBlob.size, type: audioBlob.type };
    } else {
      recordingState = "idle";
      state.audioMeta = null;
    }
    let project = projects.find(x => x.name === draft.language);
    if (!project) { project = { id: "SERVER-DRAFT-" + draft.id, name: draft.language, countryCode: "ET", progress: 0, instruction: "", requirements: "" }; projects.push(project); }
    state.project = project.id;
    state.languageChoice = draft.language;
    state.serverContributorTab = "started";
    render(draft.transcript ? "translation" : "transcription");
  } catch (error) { serverErrorToast(error); }
}

async function persistCurrentServerDraft() {
  if (!state.serverUserId || !state.signed || !["contributor", "agent"].includes(state.role) || !["record", "transcription", "translation"].includes(state.view)) return;
  if (serverDraftSavePromise) { serverDraftSaveAgain = true; return serverDraftSavePromise; }
  if (state.view === "transcription") saveTranscriptFromUI();
  if (state.view === "translation") { saveTranscriptFromUI(); saveTranslationFromUI(); }
  const form = new FormData();
  form.append("language", (projects.find(x => x.id === state.project)?.name) || state.languageChoice || "");
  form.append("translation_language", document.getElementById("translationLanguage")?.value || state.drafts?.translationLanguage || "");
  form.append("transcript", state.drafts?.transcript || "");
  form.append("translation", state.drafts?.translation || "");
  form.append("speakers_json", JSON.stringify(state.participants || []));
  form.append("recording_type", state.recordingType || "Conversation");
  form.append("consent_reference", state.consentRef || "CONSENT-TERMS-v1-AUDIO-v1");
  form.append("duration_seconds", String(Math.max(0, Math.round(Number(state.audioMeta?.duration) || elapsed || 0))));
  if (state.activeDraftId) form.append("draft_id", String(state.activeDraftId));
  if (audioBlob && !serverDraftAudioUploaded) form.append("file", audioBlob, "draft." + audioExtension(audioBlob.type));
  serverDraftSavePromise = (async () => {
    const saved = await serverFetch("/api/drafts", { method: "POST", body: form });
    state.activeDraftId = saved.id;
    serverDraftAudioUploaded = !!saved.hasAudio;
    state.serverDraftSavedAt = saved.updatedAt;
    return saved;
  })();
  try { return await serverDraftSavePromise; }
  finally {
    serverDraftSavePromise = null;
    if (serverDraftSaveAgain) { serverDraftSaveAgain = false; scheduleServerDraftSave(); }
  }
}

function scheduleServerDraftSave() {
  if (!state.serverUserId || !state.signed) return;
  clearTimeout(serverDraftSaveTimer);
  serverDraftSaveTimer = setTimeout(() => {
    persistCurrentServerDraft().catch(() => toast("Unable to save the draft. Your local text remains on this device."));
  }, 900);
}

function serverToastFromError(error) {
  const text = String(error?.message || "");
  toast(AM[text] ? text : "Could not complete this request. Please retry.");
}

function serverFormValues(form) {
  return Object.fromEntries(new FormData(form).entries());
}

async function handleServerForm(form) {
  const kind = form.dataset.serverForm;
  const ticketId = form.dataset.ticketId;
  const values = serverFormValues(form);
  if (kind === "payout-request") {
    await serverFetch("/api/payments/requests", { ...serverJson("POST", { amount: Number(values.amount) }) });
    await loadServerWorkspace(); render(state.view); toast("Payout request sent."); return;
  }
  if (kind === "ticket-create") {
    await serverFetch("/api/support/tickets", serverJson("POST", { subject: values.subject, message: values.message }));
    await loadServerWorkspace(); render(state.view); toast("Support case created."); return;
  }
  if (kind === "ticket-message") {
    await serverFetch("/api/support/tickets/" + ticketId + "/messages", serverJson("POST", { message: values.message }));
    await loadServerWorkspace(); render(state.view); toast("Message sent."); return;
  }
  if (kind === "ticket-admin") {
    const agent = values.assignedAgentId ? Number(values.assignedAgentId) : null;
    await serverFetch("/api/support/tickets/" + ticketId, serverJson("PATCH", { status: values.status, assignedAgentId: agent }));
    await loadServerWorkspace(); render(state.view); toast("Support case updated."); return;
  }
  if (kind === "user-update") {
    const formUserId = form.dataset.userId;
    const selectedAgent = values.agentId ? Number(values.agentId) : null;
    const patch = { fullName: null, role: values.role, isActive: form.elements.active.checked, agentId: selectedAgent, clearAgentAssignment: !selectedAgent };
    await serverFetch("/api/admin/users/" + formUserId, serverJson("PATCH", patch));
    await loadServerWorkspace(); render("admin"); toast("Account updated."); return;
  }
  if (kind === "site-content") {
    const content = await serverFetch("/api/admin/site-content", serverJson("PATCH", values));
    coverContent = content;
    await loadServerWorkspace(); render("admin"); toast("Cover page text saved."); return;
  }
}

async function handleServerAction(button) {
  const action = button.dataset.serverAction, id = button.dataset.id;
  if (action === "start-work") return startServerWork();
  if (action === "resume-draft") return resumeServerDraft(id);
  if (action === "delete-draft") {
    if (!confirm(tr("Delete draft") + "?")) return;
    await serverFetch("/api/drafts/" + id, { method: "DELETE" });
    await loadServerWorkspace(); render("dashboard"); return;
  }
  if (action === "export-events") {
    const blob = new Blob([JSON.stringify(serverWorkspace?.events || [], null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob), a = document.createElement("a"); a.href = url; a.download = "africorpus-event-log.json"; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); return;
  }
  if (action === "ticket-escalate") {
    await serverFetch("/api/support/tickets/" + id, serverJson("PATCH", { escalate: true }));
    await loadServerWorkspace(); render("agent-dashboard"); return;
  }
  if (action.startsWith("agent-review-")) {
    const decision = action.endsWith("accept") ? "accept" : "return";
    const comment = document.querySelector('[data-server-agent-note="' + CSS.escape(id) + '"]')?.value.trim() || "";
    await serverFetch("/api/submissions/" + id + "/agent-review", serverJson("POST", { decision, comment }));
    await loadServerWorkspace(); render("agent-dashboard"); return;
  }
  if (action.startsWith("admin-review-")) {
    const decision = action.endsWith("approve") ? "approve" : action.endsWith("return") ? "return" : "reject";
    const comment = document.querySelector('[data-server-admin-comment="' + CSS.escape(id) + '"]')?.value.trim() || "";
    const reason = document.querySelector('[data-server-admin-reason="' + CSS.escape(id) + '"]')?.value.trim() || "";
    const award = document.querySelector('[data-server-award="' + CSS.escape(id) + '"]')?.value;
    await serverFetch("/api/submissions/" + id + "/admin-review", serverJson("POST", { decision, comment, reason, awardAmount: award ? Number(award) : null, currency: "ETB" }));
    await loadServerWorkspace(); render("admin"); return;
  }
  if (action.startsWith("payment-agent-")) {
    const decision = action.endsWith("accept") ? "accept" : "return";
    const note = document.querySelector('[data-server-payment-note="' + CSS.escape(id) + '"]')?.value.trim() || "";
    await serverFetch("/api/payments/requests/" + id + "/agent-review", serverJson("POST", { decision, note }));
    await loadServerWorkspace(); render("agent-dashboard"); return;
  }
  if (action.startsWith("payment-admin-")) {
    const decision = action.endsWith("approve") ? "approve" : action.endsWith("reject") ? "reject" : "paid";
    const note = document.querySelector('[data-server-payment-note="' + CSS.escape(id) + '"]')?.value.trim() || "";
    const proofReference = document.querySelector('[data-server-payment-proof="' + CSS.escape(id) + '"]')?.value.trim() || "";
    await serverFetch("/api/admin/payments/" + id + "/review", serverJson("POST", { decision, note, proofReference }));
    await loadServerWorkspace(); render("admin");
  }
}

async function initializeServerApp() {
  installServerTranslations();
  render("home");
  await loadCoverContent();
  try {
    const result = await serverFetch("/api/auth/me");
    const user = result.user;
    state.user = user.name;
    state.role = user.role;
    state.signed = true;
    state.serverUserId = user.id;
    state.lang = user.language === "en" ? "en" : "am";
    await loadServerWorkspace();
    save();
    // Only land on the role dashboard if the person is still on the cover page. The workspace
    // query can take a few seconds, and without this guard the bootstrap would finish later
    // and yank the user back to the dashboard from wherever they had already navigated.
    if (state.view === "home") {
      render(user.role === "admin" ? "admin" : user.role === "agent" ? "agent-dashboard" : "dashboard");
    } else {
      render(state.view);
    }
  } catch {
    state.signed = false;
    state.role = "contributor";
    state.serverUserId = null;
    serverWorkspace = null;
    save();
    render("home");
  }
}

document.addEventListener("click", event => {
  const tab = event.target.closest("[data-server-tab]");
  if (tab) {
    const kind = tab.dataset.serverTabKind;
    if (kind === "contributor") state.serverContributorTab = tab.dataset.serverTab;
    if (kind === "agent") state.serverAgentTab = tab.dataset.serverTab;
    if (kind === "admin") state.serverAdminTab = tab.dataset.serverTab;
    render(kind === "contributor" ? "dashboard" : kind === "agent" ? "agent-dashboard" : "admin");
    return;
  }
  const button = event.target.closest("[data-server-action]");
  if (!button || !state.serverUserId) return;
  event.preventDefault();
  button.disabled = true;
  Promise.resolve(handleServerAction(button)).catch(serverToastFromError).finally(() => { if (button.isConnected) button.disabled = false; });
});

document.addEventListener("submit", event => {
  const form = event.target.closest("[data-server-form]");
  if (!form || !state.serverUserId) return;
  event.preventDefault();
  const submit = form.querySelector('button[type="submit"],button:not([type])');
  if (submit) submit.disabled = true;
  Promise.resolve(handleServerForm(form)).catch(serverToastFromError).finally(() => { if (submit?.isConnected) submit.disabled = false; });
});

document.addEventListener("input", event => {
  if (!state.serverUserId || !["transcription", "translation"].includes(state.view)) return;
  if (event.target.matches("[data-segment-text],#transcriptText,[data-translation-segment],#translationNotes")) {
    if (state.view === "transcription") saveTranscriptFromUI();
    else { saveTranscriptFromUI(); saveTranslationFromUI(); }
    scheduleServerDraftSave();
  }
});
