import { createTheme, createStyles, Modal, Typography } from '@material-ui/core';
import { withStyles } from '@material-ui/core/styles';
import React from 'react';
import altinnTheme from 'src/theme/altinnStudioTheme';

export interface IAltinnModalComponentProvidedProps {
  /** @ignore */
  classes: any;
  /** Text or react element shown in the header */
  headerText?: any;
  /** Text as sub-header */
  subHeaderText?: string;
  /** Boolean value of the modal being open or not */
  isOpen: boolean;
  /** Show close-icon outside modal */
  closeButtonOutsideModal?: boolean;
  /** Callback function for when the modal is closed */
  onClose: any;
  /** Boolean value for hiding the background shower */
  hideBackdrop?: boolean;
  /** Boolean value for hiding the X button in the header */
  hideCloseIcon?: boolean;
  /** Boolean value for allowing modal to close on backdrop click */
  allowCloseOnBackdropClick?: boolean;
  /** Boolean value for showing print view */
  printView?: boolean;
  children?: React.ReactNode;
}

export interface IAltinnModalComponentState {
  isOpen: boolean;
}

const theme = createTheme(altinnTheme);

const styles = createStyles({
  modal: {
    [theme.breakpoints.down('sm')]: {
      width: '95%',
    },
    [theme.breakpoints.up('md')]: {
      width: '80%',
    },
    maxWidth: '875px',
    backgroundColor: theme.altinnPalette.primary.white,
    boxShadow: theme.shadows[5],
    outline: 'none',
    marginRight: 'auto',
    marginLeft: 'auto',
    marginTop: '9.68rem',
    marginBottom: '10%',
    ['@media only print']: {
      boxShadow: '0 0 0 0 !important',
    },
  },
  header: {
    backgroundColor: altinnTheme.altinnPalette.primary.white,
    borderBottom: `1px solid ${altinnTheme.altinnPalette.primary.greyLight}`,
    paddingLeft: 12,
    '@media (min-width: 786px)': {
      paddingLeft: 96,
    },
    paddingTop: 18,
    paddingBottom: 18,
  },
  headerText: {
    fontSize: '2.8rem',
    fontWeight: 500,
    color: altinnTheme.altinnPalette.primary.black,
  },
  subHeaderText: {
    fontSize: '1.8rem',
    fontWeight:300
  },
  body: {
    paddingLeft: 12,
    paddingRight: 12,
    paddingTop: 24,
    paddingBottom: 34,
    ['@media only print']: {
      paddingLeft: 48,
    },
    '@media (min-width: 786px)': {
      paddingLeft: 96,
      paddingRight: 96,
      paddingTop: 34,
    },
  },
  iconBtn: {
    float: 'right',
    marginRight: '-11px',
    marginTop: '-27px',
  },
  iconStyling: {
    color: altinnTheme.altinnPalette.primary.black,
    fontSize: 38,
  },
  closeButtonOutsideModal: {
    position: 'relative',
    top: -60,
  },
  scroll: {
    overflow: 'overlay',
  },
});

export class AltinnModal extends React.Component<IAltinnModalComponentProvidedProps, IAltinnModalComponentState> {
  public render() {
    const { classes, printView } = this.props;
    if (!printView) {
      return (
        <Modal
          open={this.props.isOpen}
          className={this.props.classes.scroll}
          hideBackdrop={this.props.hideBackdrop}
          onBackdropClick={this.props.allowCloseOnBackdropClick === false ? null : this.props.onClose}
        >
          <div className={classes.modal}>
            <div className={classes.header}>
              <Typography className={classes.subHeaderText}>{this.props.subHeaderText}</Typography>
              <Typography className={classes.headerText}>{this.props.headerText}</Typography>
            </div>
            <div className={classes.body}>{this.props.children}</div>
          </div>
        </Modal>
      );
    } else {
      return (
        <div className={classes.modal}>
          <div className={classes.header}>
            <Typography className={classes.subHeaderText}>{this.props.subHeaderText}</Typography>
            <Typography className={classes.headerText}>{this.props.headerText}</Typography>
          </div>
          <div className={classes.body}>{this.props.children}</div>
        </div>
      );
    }
  }
}

export default withStyles(styles)(AltinnModal);
