# Admin Panel UI Mockup - Visual Description

## Dashboard View (AdminDashboardView.axaml)

```
┌─────────────────────────────────────────────────────────────────┐
│  Admin Dashboard                                                 │
│  Overview of user management and system statistics   [🔄 Refresh]│
└─────────────────────────────────────────────────────────────────┘

┌──────────────┬──────────────┬──────────────┬──────────────┐
│ ┌─────┐      │ ┌─────┐      │ ┌─────┐      │ ┌─────┐      │
│ │ 👥  │      │ │ ⏳  │      │ │ 👑  │      │ │ ✅  │      │
│ └─────┘      │ └─────┘      │ └─────┘      │ └─────┘      │
│ Total Users  │ Pending      │ Active       │ Recently     │
│     15       │ Approvals    │ Admins       │ Approved     │
│              │     3        │     2        │     1        │
│ All          │ Users        │ Admins       │ Last 24h     │
│ registered   │ awaiting     │ with         │              │
│ users        │ approval     │ access       │              │
└──────────────┴──────────────┴──────────────┴──────────────┘
   (Blue card)  (Yellow card)  (Green card)  (Pink card)

┌───────────────────────────────────────┬─────────────────────┐
│ Quick Statistics                      │ Quick Actions       │
│                                       │                     │
│ 📊 Approval Rate: 80.0%              │ 📋 System Info      │
│ [████████░░] 80%                     │                     │
│                                       │ Status: ✅ Online  │
│ 📈 User Growth                       │                     │
│ Total Users: 15 | Approved: 12 |    │ Updated:            │
│ Pending: 3                           │ [timestamp]         │
│                                       │                     │
│ 🕒 Last Approval                     │ ┌─────────────────┐ │
│ 2h 15m ago                           │ │ 👥 Manage Users │ │
│                                       │ └─────────────────┘ │
└───────────────────────────────────────┴─────────────────────┘
```

## User Management View (AdminPanelView.axaml)

```
┌─────────────────────────────────────────────────────────────────┐
│  👥 User Management                                              │
│  Manage user access requests, roles, and permissions            │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ [🔄 Refresh] [🔍 Search by name or email...]                    │
│                                                                  │
│  [All Status ▼] [All Roles ▼] [✖ Clear]    Total: 15  ⏳ Pending: 3│
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ ℹ️ Status: Loaded 15 users (3 pending approval)                 │
└─────────────────────────────────────────────────────────────────┘

┌──────────┬──────────────────┬──────────┬────────────┬───────────┬─────────┬────────────────┐
│ Full Name│ Email            │ Status   │ 👑 Role    │ Registered│ Last    │ Actions        │
│          │                  │          │            │           │ Login   │                │
├──────────┼──────────────────┼──────────┼────────────┼───────────┼─────────┼────────────────┤
│ John Doe │ john@test.com    │ ⏳ Pending│ [User ▼]  │ Jan 28    │ Never   │ ✅ Approve as │
│          │                  │          │ ╔════════╗ │           │         │    User        │
│          │                  │          │ ║  User  ║ │           │         │ 💾 Update Role │
│          │                  │          │ ╚════════╝ │           │         │                │
│          │                  │          │ (Blue      │           │         │                │
│          │                  │          │  border)   │           │         │                │
├──────────┼──────────────────┼──────────┼────────────┼───────────┼─────────┼────────────────┤
│ Jane     │ jane@test.com    │ ⏳ Pending│ [Admin ▼] │ Jan 29    │ Never   │ ✅ Approve as │
│ Smith    │                  │          │ ╔════════╗ │           │         │    Admin       │
│          │                  │          │ ║  Admin ║ │           │         │ 💾 Update Role │
│          │                  │          │ ╚════════╝ │           │         │                │
│          │                  │          │ (Blue      │           │         │                │
│          │                  │          │  border)   │           │         │                │
├──────────┼──────────────────┼──────────┼────────────┼───────────┼─────────┼────────────────┤
│ Admin    │ admin@drone.com  │ ✅ Approved│ [Admin ▼]│ Jan 15    │ Jan 30  │ 🚫 Revoke     │
│ User     │                  │ (Green)  │ ╔════════╗ │           │         │    Access      │
│          │                  │          │ ║  Admin ║ │           │         │ 💾 Update Role │
│          │                  │          │ ╚════════╝ │           │         │                │
├──────────┼──────────────────┼──────────┼────────────┼───────────┼─────────┼────────────────┤
│ Bob      │ bob@test.com     │ ✅ Approved│ [User ▼] │ Jan 20    │ Jan 29  │ 🚫 Revoke     │
│ Johnson  │                  │ (Green)  │ ╔════════╗ │           │         │    Access      │
│          │                  │          │ ║  User  ║ │           │         │ 💾 Update Role │
│          │                  │          │ ╚════════╝ │           │         │                │
└──────────┴──────────────────┴──────────┴────────────┴───────────┴─────────┴────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ 💡 Tip: Select a role from the dropdown before approving new    │
│ users. The button will show 'Approve as [Role]'. Approved users │
│ can login immediately with their assigned role.                 │
└─────────────────────────────────────────────────────────────────┘
```

## Color Scheme Visual Guide

### Card Colors:
- **White (#FFFFFF)**: Main content cards, clean background
- **Light Gray (#F9FAFB)**: Toolbars, footers, secondary backgrounds

### Badge Colors:
- **Pending Status**: Yellow background (#FEF3C7), Orange text (#F59E0B)
- **Approved Status**: Green background (#DCFCE7), Dark green text (#16A34A)
- **Admin Role Badge**: Blue background (#DBEAFE)
- **User Role Badge**: Gray background (#F3F4F6)

### Button Colors:
- **Approve Button**: Green (#10B981) → Darker green on hover (#059669)
- **Revoke Button**: Red (#EF4444) → Darker red on hover (#DC2626)
- **Secondary Buttons**: Gray (#F3F4F6) → Darker gray on hover (#E5E7EB)
- **Refresh Button**: Gray with hand cursor

### Special Styling:
- **Role Dropdown**: 
  - White background (#FFFFFF)
  - **Prominent blue border (#3B82F6) 1.5px thick** ← KEY FEATURE
  - Semi-bold font weight
  - Corner radius: 6px

- **Search Box**:
  - Watermark: "🔍 Search by name or email..."
  - Light gray border (#D1D5DB)
  - Corner radius: 6px
  - Width: 250px

## Navigation Structure

```
Left Sidebar (Bottom Section):

┌─────────────────────────┐
│ ADMIN                   │  ← Only visible to admin users
├─────────────────────────┤
│ 📊 Dashboard           │  ← NEW - Shows statistics
├─────────────────────────┤
│ 👥 User Management     │  ← Enhanced with search/filter
└─────────────────────────┘
```

## Interactive Features

### 1. Search Behavior:
```
User types "john" → Instantly shows only users with "john" in name or email
User types "admin@" → Instantly shows only users with "admin@" in email
```

### 2. Filter Behavior:
```
Status Filter = "Approved" → Shows only users with green ✅ badge
Status Filter = "Pending" → Shows only users with yellow ⏳ badge
Role Filter = "Admin" → Shows only users with Admin role
Role Filter = "User" → Shows only users with User role
```

### 3. Role Selection:
```
1. User is pending with "User" in dropdown
2. Admin changes dropdown to "Admin"
3. Button text immediately updates to "✅ Approve as Admin"
4. Admin clicks button
5. User is approved with Admin role
6. User can now login and see admin panel
```

### 4. Revoke Flow:
```
1. User is approved (green ✅ badge)
2. Admin clicks "🚫 Revoke Access"
3. User's status changes to Pending (yellow ⏳ badge)
4. User cannot login anymore
5. All user's JWT tokens are invalidated
```

## Responsive Design Notes

- Cards use grid layout that adapts to screen size
- Dashboard has 4 columns on large screens
- Minimum card height: 140px
- Proper spacing and padding throughout
- Hover effects on all interactive elements
- Smooth animations on state changes

## Accessibility Features

- Clear visual hierarchy
- Emoji icons for quick recognition
- Color-coded status indicators
- Tooltip-like watermarks in inputs
- Consistent button styling
- High contrast text colors
- Readable font sizes (12-32px range)

---

**Note**: This mockup represents the implemented UI. The actual application will render these elements using Avalonia UI controls with the exact styling defined in the AXAML files.
