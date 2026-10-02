namespace RegulusMobile;

public partial class RegulusPage : ContentPage
{
	const string RefreshGateScript = "(function(){function hidden(el){if(!el)return true;if(el.hidden)return true;var s=getComputedStyle(el);return s.display==='none'||s.visibility==='hidden';}function shown(id){return !hidden(document.getElementById(id));}if(shown('edit-dialog')||shown('comment-dialog')||shown('visit-detail')||shown('delete-confirm')||shown('login-view'))return 'busy';var list=document.getElementById('list-panel');var visitTab=document.getElementById('tab-visit');var visitView=document.getElementById('visit-view');var card=(visitTab&&visitTab.classList.contains('is-active'))||(visitView&&!hidden(visitView))||(list&&hidden(list));if(card)return 'card';if(list&&!hidden(list))return 'list';return 'busy';})()";

	int requestToken;
	bool bridgeInstalled;
	bool pageVisible;
	bool initialRefreshRequested;
	bool refreshBusy;
	DateTime lastRefreshUtc = DateTime.MinValue;
#if ANDROID
	ReceptionJsBridge? receptionBridge;
#endif

	public RegulusPage()
	{
		InitializeComponent();
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		pageVisible = true;
		if (!initialRefreshRequested)
		{
			initialRefreshRequested = true;
			RequestRefresh(true);
		}
	}

	protected override void OnDisappearing()
	{
		pageVisible = false;
		base.OnDisappearing();
	}

	protected override void OnHandlerChanged()
	{
		base.OnHandlerChanged();
		InstallBridge();
		InstallSchedulePatch();
	}

	void OnRegulusNavigating(object? sender, WebNavigatingEventArgs e)
	{
		InstallSchedulePatch();
	}

	void InstallSchedulePatch()
	{
#if ANDROID
		if (OperatingSystem.IsAndroidVersionAtLeast(26)
			&& RegulusWeb.Handler?.PlatformView is Android.Webkit.WebView native)
			ScheduleScriptClient.Install(native);
#endif
	}

	void RequestRefresh(bool automatic)
	{
		if (automatic && !pageVisible) return;
		if (CustomerPanel.IsVisible) return;
		if (automatic && DateTime.UtcNow - lastRefreshUtc < TimeSpan.FromSeconds(15)) return;
		if (refreshBusy) return;
		refreshBusy = true;
		_ = RefreshIfIdleAsync(automatic);
	}

	async Task RefreshIfIdleAsync(bool automatic)
	{
		try
		{
			var raw = await RegulusWeb.EvaluateJavaScriptAsync(RefreshGateScript);
			var state = UnwrapJs(raw);
			if (CustomerPanel.IsVisible || !pageVisible) return;
			if (state == "busy") return;
			if (automatic && state != "list") return;
			lastRefreshUtc = DateTime.UtcNow;
			RegulusWeb.Reload();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
		}
		finally
		{
			refreshBusy = false;
		}
	}

	static string UnwrapJs(string? raw)
	{
		var text = (raw ?? "").Trim();
		if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
		{
			try
			{
				return System.Text.Json.JsonSerializer.Deserialize<string>(text) ?? "";
			}
			catch (System.Text.Json.JsonException)
			{
				return "";
			}
		}
		return text;
	}

	void InstallBridge()
	{
		if (bridgeInstalled) return;
#if ANDROID
		if (RegulusWeb.Handler?.PlatformView is not Android.Webkit.WebView native) return;
		receptionBridge = new ReceptionJsBridge(OnReceptionMessage);
		native.AddJavascriptInterface(receptionBridge, "RegulusNative");
		bridgeInstalled = true;
#endif
	}

	void OnRegulusNavigated(object? sender, WebNavigatedEventArgs e)
	{
		if (e.Result != WebNavigationResult.Success) return;
		lastRefreshUtc = DateTime.UtcNow;
		InstallBridge();
		_ = RegulusWeb.EvaluateJavaScriptAsync(BridgeScript);
	}

	void OnReceptionMessage(string payload)
	{
		if (payload == "refresh")
		{
			RequestRefresh(false);
			return;
		}
		if (payload == "none")
		{
			ShowStatus("受付番号が見つかりません");
			return;
		}
		if (payload == "ambiguous")
		{
			ShowStatus("受付番号を特定できません");
			return;
		}
		if (!TryReadBridgeNumber(payload, out var kind, out var number))
		{
			ShowStatus("顧客情報取得失敗");
			return;
		}
		var token = ++requestToken;
		ShowStatus("顧客情報を取得中...");
		var page = JSystemPage.Current;
		if (page == null)
		{
			ShowStatus("顧客情報取得失敗");
			return;
		}
		page.ReadFromRegulus(number, token, (doneToken, status, card) =>
		{
			if (doneToken != requestToken) return;
			if (status == "missing")
			{
				ShowStatus("過去案件の詳細が見つかりませんでした");
				return;
			}
			if (status == "ok" && card != null)
			{
				RegulusCustomerStack.Children.Clear();
				RegulusCustomerStack.Children.Add(page.CreateSharedCustomerView(card));
				HeaderReceptionLabel.Text = card.ReceptionNo;
				ShowStatus("顧客情報取得成功");
				if (kind == "visit") _ = MarkVisitDirectAsync(card.HasDirectCollection);
				return;
			}
			ShowStatus("顧客情報取得失敗");
		});
	}

	async Task MarkVisitDirectAsync(bool on)
	{
		var flag = on ? "true" : "false";
		try
		{
			await RegulusWeb.EvaluateJavaScriptAsync("window.__regulusMarkDirect&&window.__regulusMarkDirect(" + flag + ")");
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
		}
	}

	void ShowStatus(string text)
	{
		BridgeStatusLabel.Text = "顧客情報";
		CustomerPanel.IsVisible = true;
		if (text == "顧客情報取得成功") return;
		HeaderReceptionLabel.Text = "";
		RegulusCustomerStack.Children.Clear();
		RegulusCustomerStack.Children.Add(new Label
		{
			Text = text,
			FontSize = 18,
			TextColor = Color.FromArgb("#0F172A"),
			BackgroundColor = Color.FromArgb("#EEF3F8"),
			HorizontalTextAlignment = TextAlignment.Center,
			Margin = new Thickness(0, 72, 0, 0)
		});
	}

	void OnClosePanelClicked(object? sender, EventArgs e)
	{
		CustomerPanel.IsVisible = false;
		HeaderReceptionLabel.Text = "";
		_ = RegulusWeb.EvaluateJavaScriptAsync("var d=document.getElementById('visit-detail');if(d)d.hidden=true;");
	}

	static bool TryReadBridgeNumber(string payload, out string kind, out string number)
	{
		kind = "";
		number = "";
		if (payload.Length != 10 || payload[1] != ':') return false;
		if (payload[0] == 'v') kind = "visit";
		else if (payload[0] == 's') kind = "schedule";
		else return false;
		number = payload[2..];
		return number.Length == 8 && number.All(char.IsDigit);
	}

	const string BridgeScript = """
(function(){
if(window.__regulusBridge) return;
window.__regulusBridge=1;
window.__regulusVisitCard=null;
window.__regulusVisitStamp=0;
var bgNames=['bg-red','bg-olive','bg-green','bg-cyan','bg-blue','bg-magenta','bg-yellow','bg-white','bg-lightblue'];
var bgFallback={
'bg-red':['#FF0000','#ffffff','#FF0000'],
'bg-olive':['#808000','#ffffff','#808000'],
'bg-green':['#00FF00','#0f172a','#00FF00'],
'bg-cyan':['#00FFFF','#0f172a','#00FFFF'],
'bg-blue':['#0000FF','#ffffff','#0000FF'],
'bg-magenta':['#FF00FF','#ffffff','#FF00FF'],
'bg-yellow':['#FFFF00','#0f172a','#FFFF00'],
'bg-white':['#FFFFFF','#0f172a','#cbd5e1'],
'bg-lightblue':['#00FFFF','#0f172a','#00FFFF']
};
function textOf(el){return el?(el.textContent||''):'';}
function unique(list){var out=[];for(var i=0;i<list.length;i++){if(out.indexOf(list[i])<0)out.push(list[i]);}return out;}
function pick(text){
var labeled=[];var re=/受付番号[：:]\s*([0-9]{8})(?!\d)/g;var m;
while((m=re.exec(text))) labeled.push(m[1]);
labeled=unique(labeled);
if(labeled.length===1) return labeled[0];
if(labeled.length>1) return '';
var nums=[];var re2=/(?:^|\D)([0-9]{8})(?!\d)/g;
while((m=re2.exec(text))) nums.push(m[1]);
nums=unique(nums);
if(nums.length===1) return nums[0];
return '';
}
function codeOf(text){
var labeled=[];var re=/受付番号[：:]\s*([0-9]{8})(?!\d)/g;var m;
while((m=re.exec(text))) labeled.push(m[1]);
labeled=unique(labeled);
if(labeled.length===1) return 'ok';
if(labeled.length>1) return 'ambiguous';
var nums=[];var re2=/(?:^|\D)([0-9]{8})(?!\d)/g;
while((m=re2.exec(text))) nums.push(m[1]);
nums=unique(nums);
if(nums.length===1) return 'ok';
if(nums.length>1) return 'ambiguous';
return 'none';
}
function post(kind,text){
var state=codeOf(text);
var body=state==='ok'?(kind+':'+pick(text)):state;
try{if(window.RegulusNative&&RegulusNative.postReception) RegulusNative.postReception(body);}catch(e){}
}
function hideVisit(){var detail=document.getElementById('visit-detail');if(detail) detail.hidden=true;}
function scheduleCustomerButton(node){
var link=node.closest('#edit-open-customer');
if(link) return link;
var any=node.closest('a.open-customer-button');
if(any&&any.closest('#edit-dialog')) return any;
return null;
}
function bgOn(el){
for(var i=0;i<bgNames.length;i++){if(el.classList.contains(bgNames[i])) return bgNames[i];}
return '';
}
function sampleTone(name){
var el=document.querySelector('.schedule-table td.list-cell.'+name);
if(!el) return null;
var cs=getComputedStyle(el);
if(!cs||!cs.backgroundColor||cs.backgroundColor==='transparent'||cs.backgroundColor==='rgba(0, 0, 0, 0)') return null;
return [cs.backgroundColor, cs.color, cs.borderTopColor||cs.backgroundColor];
}
function placeDirect(card){
var mark=card.querySelector('.regulus-direct');
if(!mark) return;
mark.style.color='#ffffff';
mark.style.fontWeight='700';
mark.style.backgroundColor='#dc2626';
mark.style.padding='1px 6px';
mark.style.borderRadius='4px';
mark.style.fontSize='12px';
mark.style.position='absolute';
mark.style.right='8px';
mark.style.top='50%';
mark.style.transform='translateY(-50%)';
mark.style.whiteSpace='nowrap';
card.style.position='relative';
card.style.paddingRight='78px';
}
function paintVisit(){
var root=document.getElementById('visit-cards');
if(!root) return;
root.style.gap='8px';
var cards=root.querySelectorAll('.visit-card');
for(var i=0;i<cards.length;i++){
var card=cards[i];
card.style.minHeight='0';
card.style.padding='8px 12px';
var head=card.querySelector('.visit-card-head');
var body=card.querySelector('.visit-card-text');
if(head){head.style.display='block';head.style.fontSize='13px';head.style.fontWeight='700';head.style.marginBottom='2px';head.style.lineHeight='1.2';}
if(body){body.style.display='block';body.style.fontSize='17px';body.style.fontWeight='600';body.style.lineHeight='1.3';}
if(card.classList.contains('is-empty')) continue;
var name=bgOn(card);
var tone=name?(sampleTone(name)||bgFallback[name]):null;
if(tone){
card.style.backgroundColor=tone[0];
card.style.color=tone[1];
card.style.borderColor=tone[2];
if(head) head.style.color='inherit';
if(body) body.style.color='inherit';
}
placeDirect(card);
}
}
function watch(id){
var root=document.getElementById(id);
if(!root||root.__regulusWatch) return;
root.__regulusWatch=1;
var obs=new MutationObserver(function(){paintVisit();});
obs.observe(root,{childList:true,subtree:true});
}
window.__regulusMarkDirect=function(on){
var card=window.__regulusVisitCard;
if(!card||!card.isConnected) return;
var mark=card.querySelector('.regulus-direct');
if(!on){if(mark) mark.remove();paintVisit();return;}
if(!mark){
mark=document.createElement('span');
mark.className='regulus-direct';
mark.textContent='【直集】';
var body=card.querySelector('.visit-card-text');
if(body) body.appendChild(mark); else card.appendChild(mark);
}
paintVisit();
};
document.addEventListener('click',function(ev){
var t=ev.target;if(!t||!t.closest) return;
var customer=scheduleCustomerButton(t);
if(customer){
ev.preventDefault();
ev.stopPropagation();
setTimeout(function(){post('s',textOf(document.getElementById('edit-comment-list')));},0);
return;
}
var visit=t.closest('.visit-card');
if(visit&&!visit.classList.contains('is-empty')&&visit.closest('#visit-cards')){
window.__regulusVisitCard=visit;
var stamp=++window.__regulusVisitStamp;
setTimeout(function(){
if(window.__regulusVisitStamp!==stamp) return;
var box=document.getElementById('visit-detail-comments');
var text=textOf(box);
hideVisit();
post('v',text);
},0);
return;
}
var comment=t.closest('#edit-comment-list,#comment-list');
if(!comment) return;
setTimeout(function(){post('s',textOf(comment));},0);
},true);
function renameTabs(){
var schedule=document.getElementById('tab-schedule');
var visit=document.getElementById('tab-visit');
if(schedule) schedule.textContent='一覧表示';
if(visit) visit.textContent='カード表示';
}
function styleDayBtn(btn){
btn.style.flex='0 0 auto';
btn.style.height='40px';
btn.style.padding='0 8px';
btn.style.border='1px solid #0E5A8A';
btn.style.borderRadius='8px';
btn.style.background='#0E5A8A';
btn.style.color='#fff';
btn.style.fontWeight='700';
btn.style.fontSize='13px';
btn.style.whiteSpace='nowrap';
}
function pad2(n){return n<10?'0'+n:String(n);}
function shiftVisit(days){
var input=document.getElementById('visit-date');
if(!input) return;
var m=/^(\d{4})-(\d{2})-(\d{2})$/.exec(input.value||'');
if(!m) return;
var d=new Date(Number(m[1]),Number(m[2])-1,Number(m[3]));
d.setDate(d.getDate()+days);
input.value=d.getFullYear()+'-'+pad2(d.getMonth()+1)+'-'+pad2(d.getDate());
input.dispatchEvent(new Event('change',{bubbles:true}));
}
function installDayNav(){
if(document.getElementById('regulus-prev-day')) return;
var input=document.getElementById('visit-date');
if(!input||!input.parentElement) return;
var row=document.createElement('div');
row.style.display='flex';
row.style.alignItems='center';
row.style.gap='6px';
var prev=document.createElement('button');
prev.id='regulus-prev-day';
prev.type='button';
prev.textContent='◀ 前日';
var next=document.createElement('button');
next.id='regulus-next-day';
next.type='button';
next.textContent='翌日 ▶';
styleDayBtn(prev);
styleDayBtn(next);
input.style.flex='1';
input.style.minWidth='0';
input.style.width='auto';
input.parentElement.insertBefore(row, input);
row.appendChild(prev);
row.appendChild(input);
row.appendChild(next);
prev.addEventListener('click',function(ev){ev.preventDefault();ev.stopPropagation();shiftVisit(-1);});
next.addEventListener('click',function(ev){ev.preventDefault();ev.stopPropagation();shiftVisit(1);});
}
function widenToday(){
var btn=document.getElementById('today-button');
if(!btn) return;
btn.style.setProperty('padding','0 20px','important');
btn.style.setProperty('min-width','84px','important');
btn.style.setProperty('flex','0 0 auto','important');
}
function paintHolidays(){
var nodes=document.querySelectorAll('td,th,button,div,span');
for(var i=0;i<nodes.length;i++){
var node=nodes[i];
var label=(node.textContent||'').trim();
if(!label||label.length>20||!/(休業日|祝日)/.test(label)) continue;
var cell=node.closest('td,th,.calendar-day,.day-cell')||node;
cell.style.setProperty('background-color','#dc2626','important');
cell.style.setProperty('color','#ffffff','important');
var children=cell.querySelectorAll('*');
for(var j=0;j<children.length;j++) children[j].style.setProperty('color','#ffffff','important');
}
}
function watchHolidayStyles(){
if(document.body.__regulusHolidayWatch) return;
document.body.__regulusHolidayWatch=1;
new MutationObserver(function(){paintHolidays();}).observe(document.body,{childList:true,subtree:true});
}
function installRefresh(){
if(document.getElementById('regulus-refresh')) return;
var tabs=document.querySelector('.view-tabs');
if(!tabs) return;
var btn=document.createElement('button');
btn.id='regulus-refresh';
btn.type='button';
btn.textContent='↻ 更新';
btn.className='view-tab';
btn.style.flex='0 0 auto';
btn.style.padding='0 10px';
btn.style.fontSize='13px';
btn.addEventListener('click',function(ev){
ev.preventDefault();
ev.stopPropagation();
if(window.RegulusNative&&RegulusNative.postReception) RegulusNative.postReception('refresh');
});
tabs.appendChild(btn);
}
document.addEventListener('click',function(ev){
var t=ev.target;
if(!t||!t.closest) return;
var button=t.closest('button,[role="button"],a');
var editDialog=button&&button.closest('#edit-dialog');
var commentDialog=button&&button.closest('#comment-dialog');
if(/この予定を削除/.test(textOf(button))){
var originalConfirm=window.confirm;
var firstConfirm=true;
var restoreTimer=0;
window.confirm=function(message){
if(firstConfirm){
firstConfirm=false;
var accepted=originalConfirm.call(window,message);
if(accepted){
window.confirm=function(){return true;};
restoreTimer=setTimeout(function(){window.confirm=originalConfirm;},5000);
}
return accepted;
}
return true;
};
setTimeout(function(){
if(restoreTimer) clearTimeout(restoreTimer);
window.confirm=originalConfirm;
},5000);
return;
}
if(!editDialog||!/削除/.test(textOf(button))) return;
if(!window.confirm('このスケジュールを削除してもよろしいですか？')){
ev.preventDefault();
ev.stopImmediatePropagation();
}
},true);
renameTabs();
installDayNav();
widenToday();
paintHolidays();
watchHolidayStyles();
installRefresh();
watch('visit-cards');
watch('month-list');
paintVisit();
})();
""";
}
