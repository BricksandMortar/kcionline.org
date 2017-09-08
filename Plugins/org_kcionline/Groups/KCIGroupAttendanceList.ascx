<%@ Control Language="C#" AutoEventWireup="true" CodeFile="KCIGroupAttendanceList.ascx.cs" Inherits="org_kcionline.Groups.KCIGroupAttendanceList" %>

<asp:UpdatePanel ID="pnlContent" runat="server">
    <ContentTemplate>

        <div class="panel panel-block">
                
            <div class="panel-heading clearfix">
                <h1 class="panel-title pull-left">
                    <i class="fa fa-check-square-o"></i>
                    <asp:Literal ID="lHeading" runat="server" Text="Group Attendance" />
                </h1>

                <Rock:ButtonDropDownList ID="bddlCampus" runat="server" FormGroupCssClass="panel-options pull-right" Title="All Campuses" SelectionStyle="Checkmark" OnSelectionChanged="bddlCampus_SelectionChanged" DataTextField="Name" DataValueField="Id" />

            </div>

            <div class="panel-body">

                <div class="grid grid-panel">
                    <Rock:GridFilter ID="rFilter" runat="server" OnDisplayFilterValue="rFilter_DisplayFilterValue" OnClearFilterClick="rFilter_ClearFilterClick">
                        <Rock:DateRangePicker ID="drpDates" runat="server" Label="Date Range" />
                        <Rock:RockDropDownList ID="ddlLocation" runat="server" Label="Location" DataValueField="Key" DataTextField="Value" />
                        <Rock:RockDropDownList ID="ddlSchedule" runat="server" Label="Schedule" DataValueField="Key" DataTextField="Value"  />
                    </Rock:GridFilter>

                    <Rock:Grid ID="gOccurrences" runat="server" DisplayType="Full" AllowSorting="true" RowItemText="Occurrence" OnRowSelected="gOccurrences_Edit" >
                        <Columns>
                            <Rock:DateField DataField="Date" HeaderText="Date" ItemStyle-HorizontalAlign="Left" SortExpression="Date" />
		                    <Rock:RockTemplateField HeaderText="Location" SortExpression="LocationPath,LocationName">
		                        <ItemTemplate>
		                            <%#Eval("LocationName")%><br />
		                            <small><%#Eval("LocationPath")%></small>
		                        </ItemTemplate>
		                    </Rock:RockTemplateField>
                            <Rock:RockBoundField DataField="ScheduleName" HeaderText="Schedule" SortExpression="ScheduleName" />
                            <Rock:BoolField DataField="AttendanceEntered" HeaderText="Attendance Entered" SortExpression="AttendanceEntered" />
                            <Rock:BoolField DataField="DidNotOccur" HeaderText="Didn't Meet" SortExpression="DidNotOccur" />
                            <Rock:RockBoundField DataField="DidAttendCount" HeaderText="Attendance Count" ItemStyle-HorizontalAlign="Right" DataFormatString="{0:N0}" SortExpression="DidAttendCount" />
                            <Rock:RockBoundField DataField="Percentage" HeaderText="Percent Attended" ItemStyle-HorizontalAlign="Right" DataFormatString="{0:P0}" SortExpression="Percentage" />
                            <Rock:EditField IconCssClass="fa fa-check-square-o" OnClick="gOccurrences_Edit" ToolTip="Enter Attendance" />
                            <Rock:DeleteField OnClick="gOccurrences_Delete" />
                        </Columns>
                    </Rock:Grid>
                </div>

            </div>

        </div>

    </ContentTemplate>
</asp:UpdatePanel>