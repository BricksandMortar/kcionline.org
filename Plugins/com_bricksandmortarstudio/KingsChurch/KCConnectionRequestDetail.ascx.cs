﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;
using dotless.Core.Utils;
using Microsoft.Ajax.Utilities;
using org.kcionline.bricksandmortarstudio.Utils;
using Rock;
using Rock.Constants;
using Rock.Data;
using Rock.Model;
using Rock.Security;
using Rock.Web;
using Rock.Web.Cache;
using Rock.Web.UI;
using Rock.Web.UI.Controls;
using Rock.Attribute;
using ListItem = System.Web.UI.WebControls.ListItem;

namespace RockWeb.Plugins.KingsChurch
{
    [DisplayName( "KC Connection Request Detail" )]
    [Category( "com_bricksandmortarstudio > KingsChurch" )]
    [Description( "Displays the details of the given connection request for editing state, status, etc." )]

    [LinkedPage( "Person Profile Page", "Page used for viewing a person's profile. If set a view profile button will show for each group member.", false, order: 0 )]
    [LinkedPage( "Workflow Detail Page", "Page used to display details about a workflow.", order: 1, required: false )]
    [LinkedPage( "Workflow Entry Page", "Page used to launch a new workflow of the selected type.", order: 2 )]
    [LinkedPage( "Group Detail Page", "Page used to display group details.", order: 3, required: false )]
    [WorkflowTypeField( "Reassign Workflow Type", "The workflow type fired when a person is transferred", order: 4 )]
    [TextField( "Reassign Attribute Key", "The attribute key for the workflow attribute corresponding to the new connector", true, "NewConnector", order: 5 )]
    [WorkflowTypeField( "Transfer Workflow Type", "The workflow type fired when a person is transferred", order: 6 )]
    [TextField( "Transfer Attribute Key", "The attribute key for the workflow attribute corresponding to the new connector", true, "NewConnector", order: 7 )]
    [BooleanField( "Show Workflow Buttons", "Whether to show workflow buttons or not", false, order: 8 )]
    [BooleanField( "Coordinator View", "Is the block for coordinators? (Otherwise will allow admin functionality)", true, order: 9 )]
    [PersonBadgesField("Badges", "The person badges to display in this block", false, order:6, key:"Badges")]
    [LinkedPage("Consolidation Tracker Page", "The page to return to view more followups", false, order:10)]

    public partial class KCConnectionRequestDetail : RockBlock, IDetailBlock
    {

        #region Fields

        private const string CAMPUS_SETTING = "ConnectionRequestDetail_Campus";

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the search attributes.
        /// </summary>
        /// <value>
        /// The search attributes.
        /// </value>
        public List<AttributeCache> SearchAttributes { get; set; }

        #endregion

        #region Control Methods

        /// <summary>
        /// Restores the view-state information from a previous user control request that was saved by the <see cref="M:System.Web.UI.UserControl.SaveViewState" /> method.
        /// </summary>
        /// <param name="savedState">An <see cref="T:System.Object" /> that represents the user control state to be restored.</param>
        protected override void LoadViewState( object savedState )
        {
            base.LoadViewState( savedState );

            SearchAttributes = ViewState["SearchAttributes"] as List<AttributeCache>;
            if ( SearchAttributes != null )
            {
                AddDynamicControls();
            }
        }

        /// <summary>
        /// Raises the <see cref="E:System.Web.UI.Control.Init" /> event.
        /// </summary>
        /// <param name="e">An <see cref="T:System.EventArgs" /> object that contains the event data.</param>
        protected override void OnInit( EventArgs e )
        {
            base.OnInit( e );

            gConnectionRequestActivities.DataKeyNames = new string[] { "Guid" };
            gConnectionRequestActivities.Actions.ShowAdd = true;
            gConnectionRequestActivities.Actions.AddClick += gConnectionRequestActivities_Add;
            gConnectionRequestActivities.GridRebind += gConnectionRequestActivities_GridRebind;

            rptRequestWorkflows.ItemCommand += rptRequestWorkflows_ItemCommand;

            string confirmConnectScript = @"
    $('a.js-confirm-connect').click(function( e ){
        e.preventDefault();
        Rock.dialogs.confirm('This person does not currently meet all of the requirements of the group. Are you sure you want to add them to the group?', function (result) {
            if (result) {
                window.location = e.target.href ? e.target.href : e.target.parentElement.href;
            }
        });
    });
";
            ScriptManager.RegisterStartupScript( btnSave, btnSave.GetType(), "confirmConnectScript", confirmConnectScript, true );

            // this event gets fired after block settings are updated. it's nice to repaint the screen if these settings would alter it
            this.AddConfigurationUpdateTrigger( upDetail );


            CreateBadges();
        }

        /// <summary>
        /// Raises the <see cref="E:System.Web.UI.Control.Load" /> event.
        /// </summary>
        /// <param name="e">The <see cref="T:System.EventArgs" /> object that contains the event data.</param>
        protected override void OnLoad( EventArgs e )
        {
            base.OnLoad( e );

            nbErrorMessage.Visible = false;

            if ( !Page.IsPostBack )
            {
                ShowDetail( PageParameter( "ConnectionRequestId" ).AsInteger(), PageParameter( "ConnectionOpportunityId" ).AsIntegerOrNull() );
            }
        }

        private void CreateBadges()
        {

            string badgeList = GetAttributeValue( "Badges" );
            if ( !string.IsNullOrWhiteSpace( badgeList ) )
            {
                pnlBadges.Visible = true;
                foreach ( string badgeGuid in badgeList.SplitDelimitedValues() )
                {
                    Guid guid = badgeGuid.AsGuid();
                    if ( guid != Guid.Empty )
                    {
                        var personBadge = PersonBadgeCache.Read( guid );
                        if ( personBadge != null )
                        {
                            blStatus.PersonBadges.Add( personBadge );
                        }
                    }
                }
            }
            else
            {
                pnlBadges.Visible = false;
            }

            blStatus.Visible = true;
        }

        /// <summary>
        /// Saves any user control view-state changes that have occurred since the last page postback.
        /// </summary>
        /// <returns>
        /// Returns the user control's current view state. If there is no view state associated with the control, it returns null.
        /// </returns>
        protected override object SaveViewState()
        {
            ViewState["SearchAttributes"] = hfActiveDialog.Value == "SEARCH" ? SearchAttributes : null;

            return base.SaveViewState();
        }

        /// <summary>
        /// Returns breadcrumbs specific to the block that should be added to navigation
        /// based on the current page reference.  This function is called during the page's
        /// oninit to load any initial breadcrumbs
        /// </summary>
        /// <param name="pageReference">The page reference.</param>
        /// <returns></returns>
        public override List<BreadCrumb> GetBreadCrumbs( PageReference pageReference )
        {
            var rockContext = new RockContext();
            var breadCrumbs = new List<BreadCrumb>();

            ConnectionRequest connectionRequest = null;

            int? requestId = PageParameter( "ConnectionRequestId" ).AsIntegerOrNull();
            if ( requestId.HasValue && requestId.Value > 0 )
            {
                connectionRequest = new ConnectionRequestService( rockContext ).Get( requestId.Value );
            }

            if ( connectionRequest != null )
            {
                breadCrumbs.Add( new BreadCrumb( connectionRequest.PersonAlias.Person.FullName, pageReference ) );
            }
            else
            {
                var connectionOpportunity = new ConnectionOpportunityService( rockContext ).Get( PageParameter( "ConnectionOpportunityId" ).AsInteger() );
                if ( connectionOpportunity != null )
                {
                    breadCrumbs.Add( new BreadCrumb( String.Format( "New {0} Connection Request", connectionOpportunity.Name ), pageReference ) );
                }
                else
                {
                    breadCrumbs.Add( new BreadCrumb( "New Connection Request", pageReference ) );
                }
            }

            return breadCrumbs;
        }

        #endregion

        #region Events

        #region View/Edit Panel Events

        /// <summary>
        /// Handles the Click event of the lbEdit control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected void lbEdit_Click( object sender, EventArgs e )
        {
            using ( var rockContext = new RockContext() )
            {
                ShowEditDetails( new ConnectionRequestService( rockContext ).Get( hfConnectionRequestId.ValueAsInt() ), rockContext );
            }
        }

        /// <summary>
        /// Handles the Click event of the btnCancel control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        protected void btnCancel_Click( object sender, EventArgs e )
        {
            int connectionRequestId = hfConnectionRequestId.ValueAsInt();
            if ( connectionRequestId > 0 )
            {
                ShowReadonlyDetails( new ConnectionRequestService( new RockContext() ).Get( connectionRequestId ) );
                pnlReadDetails.Visible = true;
                wpConnectionRequestActivities.Visible = true;
                pnlEditDetails.Visible = false;
                pnlTransferDetails.Visible = false;
                pnlReassignDetails.Visible = false;
            }
            else
            {
                NavigateToParentPage();
            }
        }

        /// <summary>
        /// Handles the Click event of the btnSave control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        protected void btnSave_Click( object sender, EventArgs e )
        {
            if (Page.IsValid && gpGroup.SelectedValue.AsIntegerOrNull() != null)
            {
                using (var rockContext = new RockContext())
                {
                    var connectionRequestService = new ConnectionRequestService(rockContext);
                    var groupMemberService = new GroupMemberService(rockContext);
                    var connectionActivityTypeService = new ConnectionActivityTypeService(rockContext);
                    var connectionRequestActivityService = new ConnectionRequestActivityService(rockContext);
                    var connectionRequest = connectionRequestService.Get(hfConnectionRequestId.ValueAsInt());

                    if (connectionRequest != null &&
                        connectionRequest.PersonAlias != null &&
                        connectionRequest.ConnectionOpportunity != null)
                    {
                        connectionRequest.AssignedGroupId = gpGroup.SelectedValueAsId();

                        // Only do group member placement if the request has an assigned placement group
                        if (connectionRequest.ConnectionOpportunity.GroupMemberRoleId.HasValue &&
                            connectionRequest.AssignedGroupId.HasValue)
                        {
                            // Only attempt the add if person does not already exist in group with same role
                            var groupMember =
                                groupMemberService.GetByGroupIdAndPersonIdAndGroupRoleId(
                                    connectionRequest.AssignedGroupId.Value,
                                    connectionRequest.PersonAlias.PersonId,
                                    connectionRequest.ConnectionOpportunity.GroupMemberRoleId.Value);
                            if (groupMember == null)
                            {
                                groupMember = new GroupMember();
                                groupMember.PersonId = connectionRequest.PersonAlias.PersonId;
                                groupMember.GroupRoleId =
                                    connectionRequest.ConnectionOpportunity.GroupMemberRoleId.Value;
                                groupMember.GroupMemberStatus =
                                    connectionRequest.ConnectionOpportunity.GroupMemberStatus;
                                groupMember.GroupId = connectionRequest.AssignedGroupId.Value;
                                groupMemberService.Add(groupMember);
                            }
                        }

                        // ... but always record the connection activity and change the state to connected.
                        var guid = Rock.SystemGuid.ConnectionActivityType.CONNECTED.AsGuid();
                        var connectedActivityId = connectionActivityTypeService.Queryable()
                                                                               .Where(t => t.Guid == guid)
                                                                               .Select(t => t.Id)
                                                                               .FirstOrDefault();
                        if (connectedActivityId > 0)
                        {
                            var connectionRequestActivity = new ConnectionRequestActivity();
                            connectionRequestActivity.ConnectionRequestId = connectionRequest.Id;
                            connectionRequestActivity.ConnectionOpportunityId =
                                connectionRequest.ConnectionOpportunityId;
                            connectionRequestActivity.ConnectionActivityTypeId = connectedActivityId;
                            connectionRequestActivity.ConnectorPersonAliasId = CurrentPersonAliasId;
                            connectionRequestActivityService.Add(connectionRequestActivity);
                        }

                        connectionRequest.ConnectionState = ConnectionState.Connected;

                        rockContext.SaveChanges();

                        if (!GetAttributeValue("ConsolidationTrackerPage").IsNullOrWhiteSpace())
                        {
                            var queryParms = new Dictionary<string, string> {{"success", "true"}, {"type", "place"}, {"personId", connectionRequest.PersonAlias.PersonId.ToString()} };
                            NavigateToLinkedPage("ConsolidationTrackerPage", queryParms);
                        }
                        else
                        {
                            pnlEditDetails.Visible = false;
                            ShowDetail( hfConnectionRequestId.ValueAsInt(), hfConnectionOpportunityId.ValueAsInt() );
                        }
                    }
                }
            }
            else
            {
                ShowErrorMessage("No Group Selected", "Please select a group before trying to place");
                pnlEditDetails.Visible = false;
                ShowDetail( hfConnectionRequestId.ValueAsInt(), hfConnectionOpportunityId.ValueAsInt() );
            }
            
        }

        /// <summary>
        /// Handles the Click event of the lbReassign control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected void lbReassign_Click( object sender, EventArgs e )
        {
            using ( var rockContext = new RockContext() )
            {
                var connectionRequestService = new ConnectionRequestService( rockContext );
                var connectionRequest = connectionRequestService.Get( hfConnectionRequestId.ValueAsInt() );
                if ( connectionRequest != null &&
                    connectionRequest.ConnectionOpportunity != null &&
                    connectionRequest.ConnectionOpportunity.ConnectionType != null )
                {
                    pnlReadDetails.Visible = false;
                    wpConnectionRequestActivities.Visible = false;
                    pnlReassignDetails.Visible = true;
                    if ( connectionRequest.PersonAlias != null && connectionRequest.PersonAlias.Person != null )
                    {
                        lTitle.Text = "Reassign: " + connectionRequest.PersonAlias.Person.FullName.FormatAsHtmlTitle();
                    }
                }
            }
        }

        protected void lbTransfer_Click( object sender, EventArgs e )
        {
            using ( var rockContext = new RockContext() )
            {
                var connectionRequestService = new ConnectionRequestService( rockContext );
                var connectionRequest = connectionRequestService.Get( hfConnectionRequestId.ValueAsInt() );
                if ( connectionRequest != null &&
                    connectionRequest.ConnectionOpportunity != null &&
                    connectionRequest.ConnectionOpportunity.ConnectionType != null )
                {
                    pnlReadDetails.Visible = false;
                    wpConnectionRequestActivities.Visible = false;
                    pnlTransferDetails.Visible = true;
                    if ( connectionRequest.PersonAlias != null && connectionRequest.PersonAlias.Person != null )
                    {
                        lTitle.Text = "Transfer: " + connectionRequest.PersonAlias.Person.FullName.FormatAsHtmlTitle();
                    }
                }
            }
        }

        /// <summary>
        /// Handles the ItemCommand event of the rptRequestWorkflows control.
        /// </summary>
        /// <param name="source">The source of the event.</param>
        /// <param name="e">The <see cref="RepeaterCommandEventArgs"/> instance containing the event data.</param>
        protected void rptRequestWorkflows_ItemCommand( object source, RepeaterCommandEventArgs e )
        {
            if ( e.CommandName == "LaunchWorkflow" )
            {
                using ( var rockContext = new RockContext() )
                {
                    var connectionRequest = new ConnectionRequestService( rockContext ).Get( hfConnectionRequestId.ValueAsInt() );
                    var connectionWorkflow = new ConnectionWorkflowService( rockContext ).Get( e.CommandArgument.ToString().AsInteger() );
                    if ( connectionRequest != null && connectionWorkflow != null )
                    {
                        LaunchWorkflow( rockContext, connectionRequest, connectionWorkflow );
                    }
                }
            }
        }

        #endregion

        #region Reassign Events

        /// <summary>
        /// Handles the Click event of the btnTransferSave control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected void btnReassignSave_Click( object sender, EventArgs e )
        {
            if ( ppReassign.PersonAliasId.HasValue )
            {
                using ( var rockContext = new RockContext() )
                {
                    var connectionRequestService = new ConnectionRequestService( rockContext );

                    var connectionRequest = connectionRequestService.Get( hfConnectionRequestId.ValueAsInt() );
                    if ( connectionRequest != null )
                    {

                        WorkflowType workflowType = null;
                        Guid? workflowTypeGuid = GetAttributeValue( "ReassignWorkflowType" ).AsGuidOrNull();
                        if ( workflowTypeGuid.HasValue )
                        {
                            var workflowTypeService = new WorkflowTypeService( rockContext );
                            workflowType = workflowTypeService.Get( workflowTypeGuid.Value );
                            if ( workflowType != null )
                            {
                                try
                                {

                                    List<string> workflowErrors;
                                    var workflow = Workflow.Activate( workflowType, connectionRequest.PersonAlias.Person.FullName );
                                    if ( workflow.AttributeValues != null )
                                    {
                                        if ( workflow.AttributeValues.ContainsKey( GetAttributeValue( "ReassignAttributeKey" ) ) )
                                        {
                                            var personAlias = new PersonAliasService( rockContext ).Get( ppReassign.PersonAliasId.Value );
                                            if ( personAlias != null )
                                            {
                                                workflow.AttributeValues[GetAttributeValue( "ReassignAttributeKey" )].Value = personAlias.Guid.ToString();
                                            }
                                        }
                                    }
                                    new WorkflowService( rockContext ).Process( workflow, connectionRequest, out workflowErrors );
                                }
                                catch ( Exception ex )
                                {
                                    ExceptionLogService.LogException( ex, this.Context );
                                }
                            }

                            pnlReadDetails.Visible = true;
                            wpConnectionRequestActivities.Visible = true;
                            pnlReassignDetails.Visible = false;
                            pnlTransferDetails.Visible = false;

                            ShowDetail( connectionRequest.Id, connectionRequest.ConnectionOpportunityId );
                        }
                    }
                }
            }
        }

        #endregion

        #region TransferPanel Events

        /// <summary>
        /// Handles the Click event of the btnTransferSave control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected void btnTransferSave_Click( object sender, EventArgs e )
        {
            if ( ppTransfer.PersonAliasId.HasValue )
            {
                using ( var rockContext = new RockContext() )
                {
                    var connectionRequestService = new ConnectionRequestService( rockContext );


                    var connectionRequest = connectionRequestService.Get( hfConnectionRequestId.ValueAsInt() );
                    if ( connectionRequest != null )
                    {


                        WorkflowType workflowType = null;
                        Guid? workflowTypeGuid = GetAttributeValue( "TransferWorkflowType" ).AsGuidOrNull();
                        if ( workflowTypeGuid.HasValue )
                        {
                            var workflowTypeService = new WorkflowTypeService( rockContext );
                            workflowType = workflowTypeService.Get( workflowTypeGuid.Value );
                            if ( workflowType != null )
                            {
                                try
                                {

                                    List<string> workflowErrors;
                                    var workflow = Workflow.Activate( workflowType, connectionRequest.PersonAlias.Person.FullName );
                                    if ( workflow.AttributeValues != null )
                                    {
                                        if ( workflow.AttributeValues.ContainsKey( GetAttributeValue( "TransferAttributeKey" ) ) )
                                        {
                                            var personAlias = new PersonAliasService( rockContext ).Get( ppTransfer.PersonAliasId.Value );
                                            if ( personAlias != null )
                                            {
                                                workflow.AttributeValues[GetAttributeValue( "TransferAttributeKey" )].Value = personAlias.Guid.ToString();
                                            }
                                        }
                                    }
                                    new WorkflowService( rockContext ).Process( workflow, connectionRequest, out workflowErrors );
                                }
                                catch ( Exception ex )
                                {
                                    ExceptionLogService.LogException( ex, this.Context );
                                }
                            }

                            pnlReadDetails.Visible = true;
                            wpConnectionRequestActivities.Visible = true;
                            pnlReassignDetails.Visible = false;
                            pnlTransferDetails.Visible = false;
                            if (!GetAttributeValue("ConsolidationTrackerPage").IsNullOrWhiteSpace())
                            {
                                var queryParms = new Dictionary<string, string>
                                {
                                    {"success", "true"},
                                    {"type", "transfer"},
                                    {"personId", connectionRequest.PersonAlias.PersonId.ToString()}
                                };
                                NavigateToLinkedPage("ConsolidationTrackerPage", queryParms);
                            }
                            else
                            {
                                ShowDetail(connectionRequest.Id, connectionRequest.ConnectionOpportunityId);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handles the Click event of the btnSearch control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected void btnSearch_Click( object sender, EventArgs e )
        {
            using ( var rockContext = new RockContext() )
            {
                var connectionRequestService = new ConnectionRequestService( rockContext );
                var connectionRequest = connectionRequestService.Get( hfConnectionRequestId.ValueAsInt() );
                if ( connectionRequest != null &&
                    connectionRequest.ConnectionOpportunity != null &&
                    connectionRequest.ConnectionOpportunity.ConnectionType != null )
                {
                    cblCampus.DataSource = CampusCache.All();
                    cblCampus.DataBind();

                    if ( connectionRequest.CampusId.HasValue )
                    {
                        cblCampus.SetValues( new List<string> { connectionRequest.CampusId.Value.ToString() } );
                    }

                    BindAttributes();
                    AddDynamicControls();

                    rptSearchResult.DataSource = connectionRequest.ConnectionOpportunity.ConnectionType.ConnectionOpportunities.ToList();
                    rptSearchResult.DataBind();
                    ShowDialog( "Search", true );
                }
            }
        }

        /// <summary>
        /// Handles the SaveClick event of the dlgSearch control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected void dlgSearch_SaveClick( object sender, EventArgs e )
        {
            using ( var rockContext = new RockContext() )
            {
                var connectionRequestService = new ConnectionRequestService( rockContext );
                var connectionRequest = connectionRequestService.Get( hfConnectionRequestId.ValueAsInt() );
                if ( connectionRequest != null &&
                    connectionRequest.ConnectionOpportunity != null &&
                    connectionRequest.ConnectionOpportunity.ConnectionType != null )
                {
                    var qrySearch = connectionRequest.ConnectionOpportunity.ConnectionType.ConnectionOpportunities.ToList();

                    if ( !string.IsNullOrWhiteSpace( tbSearchName.Text ) )
                    {
                        var searchTerms = tbSearchName.Text.ToLower().SplitDelimitedValues( true );
                        qrySearch = qrySearch.Where( o => searchTerms.Any( t => t.Contains( o.Name.ToLower() ) || o.Name.ToLower().Contains( t ) ) ).ToList();
                    }

                    var searchCampuses = cblCampus.SelectedValuesAsInt;
                    if ( searchCampuses.Count > 0 )
                    {
                        qrySearch = qrySearch.Where( o => o.ConnectionOpportunityCampuses.Any( c => searchCampuses.Contains( c.CampusId ) ) ).ToList();
                    }

                    // Filter query by any configured attribute filters
                    if ( SearchAttributes != null && SearchAttributes.Any() )
                    {
                        var attributeValueService = new AttributeValueService( rockContext );
                        var parameterExpression = attributeValueService.ParameterExpression;

                        foreach ( var attribute in SearchAttributes )
                        {
                            var filterControl = phAttributeFilters.FindControl( "filter_" + attribute.Id.ToString() );
                            if ( filterControl != null )
                            {
                                var filterValues = attribute.FieldType.Field.GetFilterValues( filterControl, attribute.QualifierValues, Rock.Reporting.FilterMode.SimpleFilter );
                                var expression = attribute.FieldType.Field.AttributeFilterExpression( attribute.QualifierValues, filterValues, parameterExpression );
                                if ( expression != null )
                                {
                                    var attributeValues = attributeValueService
                                        .Queryable()
                                        .Where( v => v.Attribute.Id == attribute.Id );

                                    attributeValues = attributeValues.Where( parameterExpression, expression, null );

                                    qrySearch = qrySearch.Where( w => attributeValues.Select( v => v.EntityId ).Contains( w.Id ) ).ToList();
                                }
                            }
                        }
                    }
                    rptSearchResult.DataSource = qrySearch;
                    rptSearchResult.DataBind();
                }
            }
        }

        #endregion       

        #region ConnectionRequestActivity Events

        /// <summary>
        /// Handles the Click event of the btnAddConnectionRequestActivity control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected void btnAddConnectionRequestActivity_Click( object sender, EventArgs e )
        {
            using ( var rockContext = new RockContext() )
            {
                var connectionRequestService = new ConnectionRequestService( rockContext );
                var connectionRequestActivityService = new ConnectionRequestActivityService( rockContext );
                var personAliasService = new PersonAliasService( rockContext );

                var connectionRequest = connectionRequestService.Get( hfConnectionRequestId.ValueAsInt() );
                if ( connectionRequest != null )
                {
                    int? activityTypeId = ddlActivity.SelectedValueAsId();
                    int? personAliasId = personAliasService.GetPrimaryAliasId( ddlActivityConnector.SelectedValueAsId() ?? 0 );
                    if ( activityTypeId.HasValue && personAliasId.HasValue )
                    {

                        ConnectionRequestActivity connectionRequestActivity = null;
                        Guid? guid = hfAddConnectionRequestActivityGuid.Value.AsGuidOrNull();
                        if ( guid.HasValue )
                        {
                            connectionRequestActivity = connectionRequestActivityService.Get( guid.Value );
                        }
                        if ( connectionRequestActivity == null )
                        {
                            connectionRequestActivity = new ConnectionRequestActivity();
                            connectionRequestActivity.ConnectionRequestId = connectionRequest.Id;
                            connectionRequestActivity.ConnectionOpportunityId = connectionRequest.ConnectionOpportunityId;
                            connectionRequestActivityService.Add( connectionRequestActivity );
                        }

                        connectionRequestActivity.ConnectionActivityTypeId = activityTypeId.Value;
                        connectionRequestActivity.ConnectorPersonAliasId = personAliasId.Value;
                        connectionRequestActivity.Note = tbNote.Text;

                        rockContext.SaveChanges();

                        BindConnectionRequestActivitiesGrid( connectionRequest, rockContext );
                        HideDialog();
                    }
                }
            }
        }

        /// <summary>
        /// Handles the GridRebind event of the gConnectionRequestActivities control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        private void gConnectionRequestActivities_GridRebind( object sender, EventArgs e )
        {
            using ( var rockContext = new RockContext() )
            {
                var connectionRequestService = new ConnectionRequestService( rockContext );
                var connectionRequest = connectionRequestService.Get( hfConnectionRequestId.ValueAsInt() );
                if ( connectionRequest != null )
                {
                    BindConnectionRequestActivitiesGrid( connectionRequest, rockContext );
                }
            }
        }

        /// <summary>
        /// Handles the Add event of the gConnectionRequestActivities control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        private void gConnectionRequestActivities_Add( object sender, EventArgs e )
        {
            ShowActivityDialog( Guid.Empty );
        }


        /// <summary>
        /// Handles the Edit event of the gConnectionRequestActivities control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="RowEventArgs"/> instance containing the event data.</param>
        protected void gConnectionRequestActivities_Edit( object sender, RowEventArgs e )
        {
            // only allow editing if current user created the activity, and not a system activity
            var activityGuid = e.RowKeyValue.ToString().AsGuid();
            var activity = new ConnectionRequestActivityService( new RockContext() ).Get( activityGuid );
            if ( activity != null &&
                ( activity.CreatedByPersonAliasId.Equals( CurrentPersonAliasId ) || activity.ConnectorPersonAliasId.Equals( CurrentPersonAliasId ) ) &&
                activity.ConnectionActivityType.ConnectionTypeId.HasValue )
            {
                ShowActivityDialog( activityGuid );
            }
        }

        /// <summary>
        /// Handles the Delete event of the gConnectionRequestActivities control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="RowEventArgs"/> instance containing the event data.</param>
        protected void gConnectionRequestActivities_Delete( object sender, RowEventArgs e )
        {
            using ( var rockContext = new RockContext() )
            {
                // only allow deleting if current user created the activity, and not a system activity
                var activityGuid = e.RowKeyValue.ToString().AsGuid();
                var connectionRequestActivityService = new ConnectionRequestActivityService( rockContext );
                var activity = connectionRequestActivityService.Get( activityGuid );
                if ( activity != null &&
                    ( activity.CreatedByPersonAliasId.Equals( CurrentPersonAliasId ) || activity.ConnectorPersonAliasId.Equals( CurrentPersonAliasId ) ) &&
                    activity.ConnectionActivityType.ConnectionTypeId.HasValue )
                {
                    connectionRequestActivityService.Delete( activity );
                    rockContext.SaveChanges();
                }

                var connectionRequestService = new ConnectionRequestService( rockContext );
                var connectionRequest = connectionRequestService.Get( hfConnectionRequestId.ValueAsInt() );
                BindConnectionRequestActivitiesGrid( connectionRequest, rockContext );
            }
        }

        /// <summary>
        /// Binds the connection request activities grid.
        /// </summary>
        private void BindConnectionRequestActivitiesGrid( ConnectionRequest connectionRequest, RockContext rockContext )
        {
            if ( connectionRequest != null && connectionRequest.PersonAlias != null )
            {
                var connectionRequestActivityService = new ConnectionRequestActivityService( rockContext );
                var qry = connectionRequestActivityService
                    .Queryable( "ConnectionActivityType,ConnectionOpportunity,ConnectorPersonAlias.Person" )
                    .Where( a =>
                        a.ConnectionRequest != null &&
                        a.ConnectionRequest.PersonAlias != null &&
                        a.ConnectionRequest.PersonAlias.PersonId == connectionRequest.PersonAlias.PersonId &&
                        a.ConnectionActivityType != null &&
                        a.ConnectionOpportunity != null );

                if ( connectionRequest != null &&
                    connectionRequest.ConnectionOpportunity != null &&
                    connectionRequest.ConnectionOpportunity.ConnectionType != null &&
                    connectionRequest.ConnectionOpportunity.ConnectionType.EnableFullActivityList )
                {
                    qry = qry.Where( a => a.ConnectionOpportunity.ConnectionTypeId == connectionRequest.ConnectionOpportunity.ConnectionTypeId );
                }
                else
                {
                    qry = qry.Where( a => a.ConnectionRequestId == connectionRequest.Id );
                }

                gConnectionRequestActivities.DataSource = qry.ToList()
                    .Select( a => new
                    {
                        a.Id,
                        a.Guid,
                        CreatedDate = a.CreatedDateTime,
                        Date = a.CreatedDateTime.HasValue ? a.CreatedDateTime.Value.ToShortDateString() : "",
                        Activity = a.ConnectionActivityType.Name,
                        Opportunity = a.ConnectionOpportunity.Name,
                        OpportunityId = a.ConnectionOpportunityId,
                        Connector = a.ConnectorPersonAlias != null && a.ConnectorPersonAlias.Person != null ? a.ConnectorPersonAlias.Person.FullName : "",
                        Note = a.Note,
                        CanEdit =
                                ( a.CreatedByPersonAliasId.Equals( CurrentPersonAliasId ) || a.ConnectorPersonAliasId.Equals( CurrentPersonAliasId ) ) &&
                                a.ConnectionActivityType.ConnectionTypeId.HasValue
                    } )
                    .OrderByDescending( a => a.CreatedDate )
                    .ToList();
                gConnectionRequestActivities.DataBind();
            }
        }

        #endregion

        #endregion

        #region Internal Methods

        /// <summary>
        /// Shows the detail.
        /// </summary>
        /// <param name="connectionRequestId">The connection request identifier.</param>
        public void ShowDetail( int connectionRequestId )
        {
            ShowDetail( connectionRequestId, null );
        }

        /// <summary>
        /// Shows the detail.
        /// </summary>
        /// <param name="connectionRequestId">The connection request identifier.</param>
        /// <param name="connectionOpportunityId">The connectionOpportunity id.</param>
        public void ShowDetail( int connectionRequestId, int? connectionOpportunityId )
        {
            bool editAllowed = UserCanEdit;

            // autoexpand the person picker if this is an add
            this.Page.ClientScript.RegisterStartupScript(
                this.GetType(),
                "StartupScript", @"Sys.Application.add_load(function () {

                // if the person picker is empty then open it for quick entry
                var personPicker = $('.js-authorizedperson');
                var currentPerson = personPicker.find('.picker-selectedperson').html();
                if (currentPerson != null && currentPerson.length == 0) {
                    $(personPicker).find('a.picker-label').trigger('click');
                }

            });", true );

            using ( var rockContext = new RockContext() )
            {
                var connectionOpportunityService = new ConnectionOpportunityService( rockContext );
                var connectionStatusService = new ConnectionStatusService( rockContext );

                ConnectionOpportunity connectionOpportunity = null;
                ConnectionRequest connectionRequest = null;

                if ( connectionRequestId > 0 )
                {
                    connectionRequest = new ConnectionRequestService( rockContext ).Get( connectionRequestId );
                }

                if ( connectionRequest == null )
                {
                    connectionOpportunity = connectionOpportunityService.Get( connectionOpportunityId.Value );
                    if ( connectionOpportunity != null )
                    {
                        var connectionStatus = connectionStatusService
                            .Queryable()
                            .Where( s =>
                                s.ConnectionTypeId == connectionOpportunity.ConnectionTypeId &&
                                s.IsDefault )
                            .FirstOrDefault();

                        if ( connectionStatus != null )
                        {
                            connectionRequest = new ConnectionRequest();
                            connectionRequest.ConnectionOpportunity = connectionOpportunity;
                            connectionRequest.ConnectionOpportunityId = connectionOpportunity.Id;
                            connectionRequest.ConnectionState = ConnectionState.Active;
                            connectionRequest.ConnectionStatus = connectionStatus;
                            connectionRequest.ConnectionStatusId = connectionStatus.Id;

                            int? campusId = GetUserPreference( CAMPUS_SETTING ).AsIntegerOrNull();
                            if ( campusId.HasValue )
                            {
                                connectionRequest.CampusId = campusId.Value;
                            }
                        }
                    }
                }
                else
                {
                    connectionOpportunity = connectionRequest.ConnectionOpportunity;
                }


                if ( connectionOpportunity != null && connectionRequest != null )
                {
                    if ( connectionRequest.Id != 0 && PageParameter( "PersonId" ).AsIntegerOrNull() == null )
                    {
                        var qryParams = new Dictionary<string, string>();
                        qryParams["ConnectionOpportunityId"] = PageParameter( "ConnectionOpportunityId" );
                        qryParams["ConnectionRequestId"] = PageParameter( "ConnectionRequestId" );
                        qryParams["PersonId"] = connectionRequest.PersonAlias.PersonId.ToString();

                        NavigateToPage( RockPage.Guid, qryParams );
                    }
                    else
                    {
                        hfConnectionOpportunityId.Value = connectionRequest.ConnectionOpportunityId.ToString();
                        hfConnectionRequestId.Value = connectionRequest.Id.ToString();
                        lConnectionOpportunityIconHtml.Text = string.Format( "<i class='{0}' ></i>", connectionOpportunity.IconCssClass );

                        pnlReadDetails.Visible = true;

                        if ( connectionRequest.PersonAlias != null && connectionRequest.PersonAlias.Person != null )
                        {
                            lTitle.Text = connectionRequest.PersonAlias.Person.FullName.FormatAsHtmlTitle();
                        }
                        else
                        {
                            lTitle.Text = String.Format( "New {0} Connection Request", connectionOpportunity.Name );
                        }

                        // Only users that have Edit rights to block, or edit rights to the opportunity
                        if ( !editAllowed )
                        {
                            editAllowed = connectionRequest.IsAuthorized( Authorization.EDIT, CurrentPerson );
                        }

                        // Grants edit access to those in the opportunity's connector groups
                        if ( !editAllowed && CurrentPersonId.HasValue )
                        {
                            // Grant edit access to any of those in a non campus-specific connector group
                            editAllowed = connectionOpportunity.ConnectionOpportunityConnectorGroups
                                                               .Any( g =>
                                                                    !g.CampusId.HasValue &&
                                                                    g.ConnectorGroup != null &&
                                                                    g.ConnectorGroup.Members.Any(
                                                                         m => m.PersonId == CurrentPersonId ) );
                            if ( !editAllowed )
                            {
                                //If this is a new request, grant edit access to any connector group. Otherwise, match the request's campus to the corresponding campus-specific connector group
                                if ( connectionOpportunity
                                    .ConnectionOpportunityConnectorGroups.Any( g => ( connectionRequest.Id == 0 || ( connectionRequest.CampusId.HasValue && g.CampusId == connectionRequest.CampusId.Value ) ) &&
                                         g.ConnectorGroup != null &&
                                         g.ConnectorGroup.Members.Any( m => m.PersonId == CurrentPersonId ) ) )
                                {
                                    editAllowed = true;
                                }
                            }
                        }
                    }

                    lbEdit.Visible = connectionRequest.AssignedGroupId == null;
                    lbReassign.Visible = editAllowed;
                    lbTransfer.Visible = editAllowed;
                    var isCoordinator = GetAttributeValue( "CoordinatorView" ).AsBooleanOrNull() ?? true;
                    gConnectionRequestActivities.IsDeleteEnabled = !isCoordinator;
                    gConnectionRequestActivities.Actions.ShowAdd = !isCoordinator;

                    if ( !editAllowed )
                    {
                        // User is not authorized
                        nbEditModeMessage.Text = EditModeMessage.ReadOnlyEditActionNotAllowed( ConnectionRequest.FriendlyTypeName );
                        ShowReadonlyDetails( connectionRequest );
                    }
                    else
                    {
                        nbEditModeMessage.Text = string.Empty;
                        if ( connectionRequest.Id > 0 )
                        {
                            ShowReadonlyDetails( connectionRequest );
                        }
                        else
                        {
                            ShowEditDetails( connectionRequest, rockContext );
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Shows the readonly details.
        /// </summary>
        /// <param name="connectionRequest">The connection request.</param>
        private void ShowReadonlyDetails( ConnectionRequest connectionRequest )
        {

            if ( connectionRequest.ConnectionState == ConnectionState.Inactive || connectionRequest.ConnectionState == ConnectionState.Connected )
            {
                lbReassign.Visible = false;
                lbTransfer.Visible = false;
            }

            if ( connectionRequest.PersonAlias != null && connectionRequest.PersonAlias.Person != null )
            {
                lTitle.Text = connectionRequest.PersonAlias.Person.FullName.FormatAsHtmlTitle();
            }
            else
            {
                lTitle.Text = String.Format( "New {0} Connection Request", connectionRequest.ConnectionOpportunity.Name );
            }

            btnSave.Visible = false;

            lContactInfo.Text = string.Empty;

            Person person = null;
            if ( connectionRequest != null && connectionRequest.PersonAlias != null )
            {
                person = connectionRequest.PersonAlias.Person;
            }

            if ( person != null && ( person.PhoneNumbers.Any() || !String.IsNullOrWhiteSpace( person.Email ) ) )
            {
                List<String> contactList = new List<string>();

                foreach ( PhoneNumber phoneNumber in person.PhoneNumbers )
                {
                    contactList.Add( String.Format( "{0} <font color='#808080'>{1}</font>", phoneNumber.NumberFormatted, phoneNumber.NumberTypeValue ) );
                }

                string emailTag = person.GetEmailTag( ResolveRockUrl( "/" ) );
                if ( !string.IsNullOrWhiteSpace( emailTag ) )
                {
                    contactList.Add( emailTag );
                }

                lContactInfo.Text = contactList.AsDelimited( "</br>" );
            }
            else
            {
                lContactInfo.Text = "No contact Info";
            }

            if ( person != null )
            {
                string imgTag = Rock.Model.Person.GetPersonPhotoImageTag( person, 200, 200, className: "img-thumbnail" );
                if ( person.PhotoId.HasValue )
                {
                    lPortrait.Text = string.Format( "<a href='{0}'>{1}</a>", person.PhotoUrl, imgTag );
                }
                else
                {
                    lPortrait.Text = imgTag;
                }
            }
            else
            {
                lPortrait.Text = string.Empty;
                ;
            }
            
            lRequestDate.Text = connectionRequest != null && connectionRequest.CreatedDateTime.HasValue ? connectionRequest.CreatedDateTime.Value.ToShortDateString() : string.Empty;
            if ( connectionRequest != null && connectionRequest.AssignedGroup != null )
            {
                var qryParams = new Dictionary<string, string>();
                qryParams.Add( "GroupId", connectionRequest.AssignedGroup.Id.ToString() );

                string url = LinkedPageUrl( "GroupDetailPage", qryParams );

                lPlacementGroup.Text = !string.IsNullOrWhiteSpace( url ) ?
                    string.Format( "<a href='{0}'>{1}</a>", url, connectionRequest.AssignedGroup.Name ) :
                    connectionRequest.AssignedGroup.Name;
            }
            else
            {
                lPlacementGroup.Text = "No group assigned";
            }

            if ( connectionRequest.ConnectorPersonAlias != null &&
                connectionRequest.ConnectorPersonAlias.Person != null )
            {
                lConnector.Text = connectionRequest.ConnectorPersonAlias.Person.FullName;
            }
            else
            {
                lConnector.Text = "No connector assigned";
            }

            SetConsolidatorText(connectionRequest);

            hlState.Visible = true;
            hlState.Text = connectionRequest.ConnectionState.ConvertToString();
            hlState.LabelType = connectionRequest.ConnectionState == ConnectionState.Inactive ? LabelType.Danger :
                ( connectionRequest.ConnectionState == ConnectionState.FutureFollowUp ? LabelType.Info : LabelType.Success );

            hlStatus.Visible = true;
            hlStatus.Text = connectionRequest.ConnectionStatus.Name;
            hlStatus.LabelType = connectionRequest.ConnectionStatus.IsCritical ? LabelType.Warning : LabelType.Type;

            hlOpportunity.Text = connectionRequest.ConnectionOpportunity != null ? connectionRequest.ConnectionOpportunity.Name : string.Empty;
            hlCampus.Text = connectionRequest.Campus != null ? connectionRequest.Campus.Name : string.Empty;

            pnlWorkflows.Visible = GetAttributeValue( "ShowWorkflowButtons" ).AsBoolean();

            if ( connectionRequest.ConnectionOpportunity != null )
            {
                var connectionWorkflows = connectionRequest.ConnectionOpportunity.ConnectionWorkflows.Union( connectionRequest.ConnectionOpportunity.ConnectionType.ConnectionWorkflows );
                var manualWorkflows = connectionWorkflows
                    .Where( w =>
                        w.TriggerType == ConnectionWorkflowTriggerType.Manual &&
                        w.WorkflowType != null )
                    .OrderBy( w => w.WorkflowType.Name )
                    .Distinct();

                if ( manualWorkflows.Any() )
                {
                    lblWorkflows.Visible = true;
                    rptRequestWorkflows.DataSource = manualWorkflows.ToList();
                    rptRequestWorkflows.DataBind();
                }
                else
                {
                    lblWorkflows.Visible = false;
                }
            }

            BindConnectionRequestActivitiesGrid( connectionRequest, new RockContext() );
        }

        private void SetConsolidatorText(ConnectionRequest connectionRequest)
        {
            if (connectionRequest == null || connectionRequest.PersonAlias == null)
            {
                return;
            }

            var rockContext = new RockContext();
            var consolidatedByRole = new GroupTypeRoleService(rockContext).Get(org.kcionline.bricksandmortarstudio.SystemGuid.GroupTypeRole.CONSOLIDATED_BY.AsGuid());
            if (consolidatedByRole == null)
            {
                return;
            }
            var consolidators =
                new GroupMemberService(rockContext).GetKnownRelationship(connectionRequest.PersonAlias.PersonId,
                                                       consolidatedByRole.Id).ToList().Select( gm => gm.Person.FullName);

            lConsolidator.Text = consolidators.Any() ? consolidators.Count() > 1 ? consolidators.JoinStrings(",") : consolidators.FirstOrDefault() : "Could Not Find Consolidator";
        }

        /// <summary>
        /// Shows the edit details.
        /// </summary>
        /// <param name="_connectionRequest">The _connection request.</param>
        private void ShowEditDetails( ConnectionRequest connectionRequest, RockContext rockContext )
        {
            btnSave.Visible = true;
            pnlReadDetails.Visible = false;
            wpConnectionRequestActivities.Visible = false;
            pnlEditDetails.Visible = true;
            lRequestor.Text = connectionRequest.PersonAlias.Person.FullName;
            if ( connectionRequest.ConnectorPersonAliasId != null )
            {
                lConnector.Text = connectionRequest.ConnectorPersonAlias.Person.FullName;
                lConnector.Visible = true;
            }

            SetConsolidatorText(connectionRequest);


            var availableGroups = LineQuery.GetCellGroupsInLine(CurrentPerson, rockContext, false);
            gpGroup.DataValueField = "Id";
            gpGroup.DataTextField = "Name";
            gpGroup.DataSource = availableGroups.OrderBy(g => g.Name).ToList();
            gpGroup.DataBind();

            if (connectionRequest.AssignedGroupId.HasValue &&
                availableGroups.Any(g => g.Id == connectionRequest.AssignedGroupId.Value))
            {
                gpGroup.SetValue(connectionRequest.AssignedGroup.Guid);
            }
            else if (connectionRequest.AssignedGroupId.HasValue && connectionRequest.AssignedGroupId != 0)
            {
                btnSave.Enabled = false;
                gpGroup.Enabled = false;
            }
        }

        /// <summary>
        /// Binds the attributes.
        /// </summary>
        private void BindAttributes()
        {
            using ( var rockContext = new RockContext() )
            {
                var connectionRequestService = new ConnectionRequestService( rockContext );
                var connectionRequest = connectionRequestService.Get( hfConnectionRequestId.ValueAsInt() );
                if ( connectionRequest != null &&
                    connectionRequest.ConnectionOpportunity != null )
                {
                    // Parse the attribute filters 
                    SearchAttributes = new List<AttributeCache>();

                    int entityTypeId = new ConnectionOpportunity().TypeId;
                    foreach ( var attributeModel in new AttributeService( rockContext ).Queryable()
                        .Where( a =>
                            a.EntityTypeId == entityTypeId &&
                            a.EntityTypeQualifierColumn.Equals( "ConnectionTypeId", StringComparison.OrdinalIgnoreCase ) &&
                            a.EntityTypeQualifierValue.Equals( connectionRequest.ConnectionOpportunity.ConnectionTypeId.ToString() ) &&
                            a.AllowSearch )
                        .OrderBy( a => a.Order )
                        .ThenBy( a => a.Name ) )
                    {
                        SearchAttributes.Add( AttributeCache.Read( attributeModel ) );
                    }
                }
            }
        }

        /// <summary>
        /// Adds the attribute columns.
        /// </summary>
        private void AddDynamicControls()
        {
            // Clear the filter controls
            phAttributeFilters.Controls.Clear();

            if ( SearchAttributes != null )
            {
                foreach ( var attribute in SearchAttributes )
                {
                    var control = attribute.FieldType.Field.FilterControl( attribute.QualifierValues, "filter_" + attribute.Id.ToString(), false, Rock.Reporting.FilterMode.SimpleFilter );
                    if ( control != null )
                    {
                        if ( control is IRockControl )
                        {
                            var rockControl = ( IRockControl ) control;
                            rockControl.Label = attribute.Name;
                            rockControl.Help = attribute.Description;
                            phAttributeFilters.Controls.Add( control );
                        }
                        else
                        {
                            var wrapper = new RockControlWrapper();
                            wrapper.ID = control.ID + "_wrapper";
                            wrapper.Label = attribute.Name;
                            wrapper.Controls.Add( control );
                            phAttributeFilters.Controls.Add( wrapper );
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Shows the activity dialog.
        /// </summary>
        /// <param name="activityGuid">The activity unique identifier.</param>
        private void ShowActivityDialog( Guid activityGuid )
        {
            ddlActivity.Items.Clear();
            ddlActivity.Items.Add( new ListItem( string.Empty, string.Empty ) );

            var connectors = new Dictionary<int, Person>();
            ConnectionRequestActivity activity = null;

            using ( var rockContext = new RockContext() )
            {
                var connectionRequestService = new ConnectionRequestService( rockContext );
                var connectionRequest = connectionRequestService.Get( hfConnectionRequestId.ValueAsInt() );
                if ( connectionRequest != null &&
                    connectionRequest.ConnectionOpportunity != null &&
                    connectionRequest.ConnectionOpportunity.ConnectionType != null )
                {
                    foreach ( var activityType in connectionRequest.ConnectionOpportunity.ConnectionType.ConnectionActivityTypes.OrderBy( a => a.Name ) )
                    {
                        if ( activityType.IsAuthorized( Authorization.VIEW, CurrentPerson ) )
                        {
                            ddlActivity.Items.Add( new ListItem( activityType.Name, activityType.Id.ToString() ) );
                        }
                    }

                    connectionRequest.ConnectionOpportunity.ConnectionOpportunityConnectorGroups
                        .Where( g =>
                            !g.CampusId.HasValue ||
                            !connectionRequest.CampusId.HasValue ||
                            g.CampusId.Value == connectionRequest.CampusId.Value )
                        .SelectMany( g => g.ConnectorGroup.Members )
                        .Select( m => m.Person )
                        .ToList()
                        .ForEach( p => connectors.AddOrIgnore( p.Id, p ) );
                }

                if ( activityGuid != Guid.Empty )
                {
                    activity = new ConnectionRequestActivityService( rockContext ).Get( activityGuid );
                    if ( activity != null && activity.ConnectorPersonAlias != null && activity.ConnectorPersonAlias.Person != null )
                    {
                        connectors.AddOrIgnore( activity.ConnectorPersonAlias.Person.Id, activity.ConnectorPersonAlias.Person );
                    }
                }
            }

            if ( CurrentPerson != null )
            {
                connectors.AddOrIgnore( CurrentPerson.Id, CurrentPerson );
            }

            ddlActivity.SetValue( activity != null ? activity.ConnectionActivityTypeId : 0 );

            ddlActivityConnector.Items.Clear();
            connectors
                .ToList()
                .OrderBy( p => p.Value.LastName )
                .ThenBy( p => p.Value.NickName )
                .ToList()
                .ForEach( c =>
                    ddlActivityConnector.Items.Add( new ListItem( c.Value.FullName, c.Key.ToString() ) ) );

            ddlActivityConnector.SetValue(
                activity != null && activity.ConnectorPersonAlias != null ?
                activity.ConnectorPersonAlias.PersonId : CurrentPersonId ?? 0 );

            tbNote.Text = activity != null ? activity.Note : string.Empty;

            hfAddConnectionRequestActivityGuid.Value = activityGuid.ToString();
            if ( activityGuid == Guid.Empty )
            {
                dlgConnectionRequestActivities.Title = "Add Activity";
                dlgConnectionRequestActivities.SaveButtonText = "Add";
            }
            else
            {
                dlgConnectionRequestActivities.Title = "Edit Activity";
                dlgConnectionRequestActivities.SaveButtonText = "Save";
            }

            ShowDialog( "ConnectionRequestActivities", true );
        }

        /// <summary>
        /// Shows the dialog.
        /// </summary>
        /// <param name="dialog">The dialog.</param>
        /// <param name="setValues">if set to <c>true</c> [set values].</param>
        private void ShowDialog( string dialog, bool setValues = false )
        {
            hfActiveDialog.Value = dialog.ToUpper().Trim();
            ShowDialog( setValues );
        }

        /// <summary>
        /// Shows the dialog.
        /// </summary>
        /// <param name="setValues">if set to <c>true</c> [set values].</param>
        private void ShowDialog( bool setValues = false )
        {
            switch ( hfActiveDialog.Value )
            {
                case "CONNECTIONREQUESTACTIVITIES":
                    dlgConnectionRequestActivities.Show();
                    break;

                case "SEARCH":
                    dlgSearch.Show();
                    break;
            }
        }

        /// <summary>
        /// Hides the dialog.
        /// </summary>
        private void HideDialog()
        {
            switch ( hfActiveDialog.Value )
            {
                case "CONNECTIONREQUESTACTIVITIES":
                    dlgConnectionRequestActivities.Hide();
                    break;

                case "SEARCH":
                    dlgSearch.Hide();
                    break;
            }

            hfActiveDialog.Value = string.Empty;
        }

        /// <summary>
        /// Shows the error message.
        /// </summary>
        /// <param name="title">The title.</param>
        /// <param name="message">The message.</param>
        private void ShowErrorMessage( string title, string message )
        {
            nbErrorMessage.Title = title;
            nbErrorMessage.Text = string.Format( "<p>{0}</p>", message );
            nbErrorMessage.NotificationBoxType = NotificationBoxType.Danger;
            nbErrorMessage.Visible = true;
        }

        /// <summary>
        /// Launches the workflow.
        /// </summary>
        /// <param name="rockContext">The rock context.</param>
        /// <param name="connectionWorkflow">The connection workflow.</param>
        /// <param name="name">The name.</param>
        private void LaunchWorkflow( RockContext rockContext, ConnectionRequest connectionRequest, ConnectionWorkflow connectionWorkflow )
        {
            if ( connectionRequest != null && connectionWorkflow != null && connectionWorkflow.WorkflowType != null )
            {
                var workflow = Workflow.Activate( connectionWorkflow.WorkflowType, connectionWorkflow.WorkflowType.WorkTerm, rockContext );
                if ( workflow != null )
                {
                    var workflowService = new WorkflowService( rockContext );

                    List<string> workflowErrors;
                    if ( workflowService.Process( workflow, connectionRequest, out workflowErrors ) )
                    {
                        if ( workflow.Id != 0 )
                        {
                            ConnectionRequestWorkflow connectionRequestWorkflow = new ConnectionRequestWorkflow();
                            connectionRequestWorkflow.ConnectionRequestId = connectionRequest.Id;
                            connectionRequestWorkflow.WorkflowId = workflow.Id;
                            connectionRequestWorkflow.ConnectionWorkflowId = connectionWorkflow.Id;
                            connectionRequestWorkflow.TriggerType = connectionWorkflow.TriggerType;
                            connectionRequestWorkflow.TriggerQualifier = connectionWorkflow.QualifierValue;
                            new ConnectionRequestWorkflowService( rockContext ).Add( connectionRequestWorkflow );

                            rockContext.SaveChanges();

                            if ( workflow.HasActiveEntryForm( CurrentPerson ) )
                            {
                                var qryParam = new Dictionary<string, string>();
                                qryParam.Add( "WorkflowTypeId", connectionWorkflow.WorkflowType.Id.ToString() );
                                qryParam.Add( "WorkflowId", workflow.Id.ToString() );
                                NavigateToLinkedPage( "WorkflowEntryPage", qryParam );
                            }
                            else
                            {
                                mdWorkflowLaunched.Show( string.Format( "A '{0}' workflow has been started.",
                                    connectionWorkflow.WorkflowType.Name ), ModalAlertType.Information );
                            }

                            ShowDetail( PageParameter( "ConnectionRequestId" ).AsInteger(), PageParameter( "ConnectionOpportunityId" ).AsIntegerOrNull() );
                        }
                        else
                        {
                            mdWorkflowLaunched.Show( string.Format( "A '{0}' workflow was processed (but not persisted).",
                                connectionWorkflow.WorkflowType.Name ), ModalAlertType.Information );
                        }
                    }
                    else
                    {
                        mdWorkflowLaunched.Show( "Workflow Processing Error(s):<ul><li>" + workflowErrors.AsDelimited( "</li><li>" ) + "</li></ul>", ModalAlertType.Information );
                    }
                }
            }
        }

        #endregion
    }
}